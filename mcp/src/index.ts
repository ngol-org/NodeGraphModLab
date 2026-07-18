import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { readFileSync, readdirSync, existsSync, writeFileSync, mkdirSync } from "fs";
import { join, dirname, isAbsolute, resolve } from "path";
import { fileURLToPath } from "url";
import { z } from "zod";
import { BudgetTracker } from "./budgetTracker.js";
import { NgolClient } from "./ngolClient.js";
import { ReminderManager } from "./reminderManager.js";

const __dirname = dirname(fileURLToPath(import.meta.url));
// NGOL_DOCS_DIR 未設定時は同梱の docs/ を使う（既存動作を維持）
const mcpDocsDir = process.env.NGOL_DOCS_DIR
  ? resolve(process.env.NGOL_DOCS_DIR)
  : join(__dirname, "..", "docs");

// ホットリロード対象の cs フォルダ
// NGOL_SCRIPTS_DIR 環境変数が未設定の場合、接続後に welcome メッセージから自動取得する
const envScriptsDir = process.env.NGOL_SCRIPTS_DIR ?? "";
function getScriptsDir(): string {
  const pluginDir = client.detectedPluginDir;
  if (!envScriptsDir) {
    return pluginDir ? join(pluginDir, "Nodes", "CustomNodes", "cs") : "";
  }
  if (isAbsolute(envScriptsDir) || !pluginDir) return envScriptsDir;
  return resolve(pluginDir, envScriptsDir);
}

const SAVE_PROPOSAL = `
---
💾 Save options (tell me if you want any of these)
• Save node source: save_node_source to persist to Nodes/CustomNodes/cs/ (hot-reload applies immediately, no restart needed)
• Save graph: save_graph to persist this graph to graphs/ (reusable from WebUI)
• Update patterns: append new findings/samples to mcp/docs/analysis-guide.md or examples/`;

const budget = new BudgetTracker();
const client = new NgolClient(
  process.env.NGOL_WS_URL ?? "ws://127.0.0.1:11156/ws"
);
const MAX_CHARS = parseInt(process.env.NGOL_MAX_RESPONSE_CHARS ?? "8000");
const reminders = new ReminderManager(process.env.NGOL_REMINDERS_FILE);

interface NodeTypeInfo {
  id: string;
  category: string;
  displayName: string;
  nodeVersion?: string;
  description?: string;
  ports: unknown[];
  filePath?: string;
}

let nodeCache: NodeTypeInfo[] | null = null;

async function ensureConnected(): Promise<void> {
  if (!client.isConnected()) {
    await client.connect();
  }
}

async function ensureNodeCache(): Promise<NodeTypeInfo[]> {
  if (nodeCache) return nodeCache;
  await ensureConnected();
  const resp = await client.sendAndWait(
    { type: "get_node_list" },
    "node_list_response"
  );
  nodeCache = (resp["nodes"] as NodeTypeInfo[]) ?? [];
  return nodeCache;
}

function respond(text: string, toolName?: string) {
  const body =
    text.length > MAX_CHARS
      ? text.slice(0, MAX_CHARS) + `\n...[TRUNCATED — use more specific parameters]`
      : text;
  const reminder = toolName ? reminders.pick(toolName) : null;
  const reminderPrefix = reminder ? `${reminder}\n\n---\n` : "";
  return { content: [{ type: "text" as const, text: reminderPrefix + body + budget.statusSuffix() }] };
}

async function call<T>(fn: () => Promise<T>): Promise<T> {
  budget.consume();
  await ensureConnected();
  try {
    return await fn();
  } catch (err) {
    const msg = (err as Error).message ?? "unknown error";
    const lines = [`Execution failed: ${msg}`];
    const hostLog = readHostLog(30);
    if (hostLog) lines.push("\n--- Host Log (tail 30) ---\n" + hostLog);
    return respond(lines.join("\n")) as unknown as T;
  }
}

function readHostLog(tail = 30): string {
  const pluginDir = client.detectedPluginDir;
  if (!pluginDir) return "";
  // pluginDir の2階層上にホストのログファイルがある一般的な配置を想定したヒューリスティック。
  // 該当しないホストでは単に見つからず catch されるだけで、無害にフォールバックする。
  const logPath = join(pluginDir, "..", "..", "LogOutput.log");
  try {
    const content = readFileSync(logPath, "utf-8");
    const lines = content.split(/\r?\n/);
    return lines.slice(-tail).join("\n");
  } catch {
    return "";
  }
}

function listExamples(examplesDir: string): string {
  if (!existsSync(examplesDir)) return "(no examples yet)";
  const files = readdirSync(examplesDir).filter((f) => f.endsWith(".json"));
  if (files.length === 0) return "(no examples yet)";
  return files
    .map((f) => {
      try {
        const raw = readFileSync(join(examplesDir, f), "utf-8");
        return `### ${f}\n\`\`\`json\n${raw.slice(0, 2000)}\n\`\`\``;
      } catch {
        return `### ${f}\n(failed to read)`;
      }
    })
    .join("\n\n");
}

const server = new McpServer({
  name: "ngol-mcp-server",
  version: "1.0.0",
});

// 1. get_available_nodes
server.tool(
  "get_available_nodes",
  "Fetch all node types from the game and cache them internally. " +
  "Returns only the count — node data is NOT sent to AI. " +
  "Use search_nodes(keyword) to find nodes by name, get_node_detail(nodeId) for port specs. " +
  "Call again after hot-reload to refresh the cache.",
  {},
  async () => {
    return call(async () => {
      const resp = await client.sendAndWait(
        { type: "get_node_list" },
        "node_list_response"
      );
      nodeCache = (resp["nodes"] as NodeTypeInfo[]) ?? [];
      const rt = client.detectedRuntimeType ?? "Unknown";
      const pluginDir = client.detectedPluginDir ?? "(unknown)";
      return respond(
        `Runtime: ${rt}\n` +
        `Plugin dir: ${pluginDir}\n` +
        `Node cache updated: ${nodeCache.length} nodes cached.\n` +
        `Use search_nodes(keyword) to find nodes, get_node_detail(nodeId) for port details.`
      );
    });
  }
);

// 1a. search_nodes
server.tool(
  "search_nodes",
  "Search cached node types by keyword (case-insensitive, matched against id / displayName / category). " +
  "Returns id + displayName + category for each match — no port details. " +
  "Call get_available_nodes first, or this tool will auto-fetch. " +
  "Then call get_node_detail(nodeId) to see port definitions.",
  {
    keyword: z.string().describe("Search keyword (e.g. 'camera', 'window', 'animator')"),
  },
  async ({ keyword }) => {
    return call(async () => {
      const nodes = await ensureNodeCache();
      const kw = keyword.toLowerCase();
      const hits = nodes.filter(
        (n) =>
          n.id.toLowerCase().includes(kw) ||
          n.displayName.toLowerCase().includes(kw) ||
          n.category.toLowerCase().includes(kw)
      );
      if (hits.length === 0) {
        return respond(`0 matches for "${keyword}".`);
      }
      const lines = hits.map((n) => `${n.id}  |  ${n.displayName}  |  ${n.category}`);
      return respond(
        `${hits.length} match(es) for "${keyword}":\n` +
        `id  |  displayName  |  category\n` +
        lines.join("\n")
      );
    });
  }
);

// 1b. get_node_detail
server.tool(
  "get_node_detail",
  "Get full detail (ports, description, category) for a specific node type by its exact ID. " +
  "Call search_nodes first to find the nodeId. " +
  "Auto-fetches the cache if get_available_nodes has not been called yet.",
  {
    nodeId: z.string().describe("Exact node type ID (e.g. 'ngol.logic.add')"),
  },
  async ({ nodeId }) => {
    return call(async () => {
      const nodes = await ensureNodeCache();
      const node = nodes.find((n) => n.id === nodeId);
      if (!node) {
        return respond(`Node not found: "${nodeId}". Use search_nodes to find the correct ID.`);
      }
      return respond(JSON.stringify(node, null, 2));
    });
  }
);

// 2. get_analysis_guide
server.tool(
  "get_analysis_guide",
  "Get analysis output pattern guide and example graphs. Call this to decide how to output analysis results from your analysis graph.",
  {},
  async () => {
    budget.consume();
    const guidePath = join(mcpDocsDir, "analysis-guide.md");
    const guide = existsSync(guidePath)
      ? readFileSync(guidePath, "utf-8")
      : "(analysis-guide.md not found)";
    const examples = listExamples(join(mcpDocsDir, "examples"));
    return respond(`${guide}\n\n## Available Examples\n${examples}`);
  }
);

// 3. compile_node
server.tool(
  "compile_node",
  "Compile a C# node class, save its source to disk (hot-reload, no restart needed), and register it. Returns nodeId on success. " +
  "Source is always saved to NGOL_SCRIPTS_DIR/<folder>/<ClassName>.cs — default folder is 'ai_generated'. " +
  "Use folder='T-XXXX' for task-specific analysis nodes so the source survives session changes. " +
  "IMPORTANT: Before writing C# source, call get_node_dev_guide to learn the correct API.",
  {
    source: z.string().describe("C# source code for the node class"),
    className: z.string().describe("The node class name (must match class in source)"),
    folder: z.string().optional().describe(
      "Subfolder under NGOL_SCRIPTS_DIR to save source file (e.g. 'experiments'). Default: 'ai_generated'. " +
      "The file is saved as <folder>/<ClassName>.cs and picked up by hot-reload automatically."
    ),
  },
  async ({ source, className, folder }) => {
    return call(async () => {
      // Save source file for hot-reload and session persistence
      const scriptsDir = getScriptsDir();
      let savedPath: string | null = null;
      let hotReloadPromise: Promise<Record<string, unknown>> | null = null;

      if (scriptsDir) {
        const targetFolder = folder ?? "ai_generated";
        const targetDir = join(scriptsDir, targetFolder);
        try {
          mkdirSync(targetDir, { recursive: true });
          savedPath = join(targetDir, `${className}.cs`);
          // ファイル書き込み前にプッシュ待機を登録（競合防止: FSW hot-reload 完了を確実に受け取る）
          hotReloadPromise = client.waitForPush(
            ["node_list_updated", "script_compile_error"],
            6000  // debounce 500ms + コンパイル時間 + バッファ
          );
          writeFileSync(savedPath, source, "utf-8");
        } catch (err) {
          savedPath = null;
          hotReloadPromise = null;
        }
      }

      // WebSocket compile for immediate error feedback
      const resp = await client.sendAndWait(
        { type: "compile_node", source, className, persist: false },
        "compile_node_response",
        30000
      );
      const compileText = resp["success"]
        ? `Compiled: ${resp["nodeId"]}`
        : `Failed: ${resp["errorMessage"]}\n${((resp["diagnostics"] as string[] | undefined) ?? []).join("\n")}`;
      const saveText = savedPath
        ? `\nSource saved: ${savedPath}`
        : scriptsDir
          ? `\nWARN: Source file write failed`
          : `\nWARN: scriptsDir not resolved — source not saved to disk`;

      // ホットリロード完了を待機（FileSystemWatcher の 2 重実行クラッシュを防止）
      let hotReloadText = "";
      if (hotReloadPromise) {
        try {
          const pushMsg = await hotReloadPromise;
          const msgType = pushMsg["type"] as string;
          if (msgType === "node_list_updated") {
            const ids = (pushMsg["updatedNodeTypeIds"] as string[] | undefined) ?? [];
            nodeCache = null;  // ノードキャッシュを無効化
            hotReloadText = `\nHot-reload: registered ${ids.length > 0 ? ids.join(", ") : "(none)"}`;
          } else {
            // script_compile_error
            const errMsg = (pushMsg["errorMessage"] as string | undefined) ?? "unknown error";
            const diags = (pushMsg["diagnostics"] as string[] | undefined) ?? [];
            const diagText = diags.length > 0
              ? "\nDiagnostics:\n" + diags.map(d => `  ${d}`).join("\n")
              : "";
            hotReloadText = `\nHot-reload failed: ${errMsg}${diagText}`;
          }
        } catch {
          hotReloadText = `\nWARN: Hot-reload confirmation timed out (6s).`;
        }
      }

      return respond(compileText + saveText + hotReloadText, "compile_node");
    });
  }
);

// 3b. get_node_dev_guide
server.tool(
  "get_node_dev_guide",
  "Get the full C# node development guide (INode API, NodeType/NodePort attributes, IExecutionContext, data types, Unity API usage, etc.). Call this before writing any C# node source for compile_node.",
  {},
  async () => {
    budget.consume();
    const rt = client.detectedRuntimeType ?? "Unknown";
    const runtimeNote = rt === "IL2CPP"
      ? `> **⚠️ Connected game is IL2CPP.**\n> Unity API calls (RenderTexture, Texture2D, GL.Clear, ReadPixels, Apply, GetRawTextureData, Object.Destroy, etc.) **must always be made on the main thread via \`ctx.MainThreadDispatch()\` + \`ManualResetEventSlim\`.**\n> Calling directly from a background thread will cause a crash or timeout.\n`
      : rt === "Mono"
      ? `> **ℹ️ Connected game is Mono.**\n> Mono tolerates calling Unity API from a background thread, but using \`ctx.MainThreadDispatch()\` is still recommended for IL2CPP compatibility.\n`
      : "";
    const pluginDir = client.detectedPluginDir ?? "(unknown)";
    const path = join(mcpDocsDir, "node-dev-reference.md");
    const content = existsSync(path)
      ? readFileSync(path, "utf-8")
      : "(node-dev-reference.md not found)";
    return respond(`Runtime: ${rt}\nPlugin dir: ${pluginDir}\n\n${runtimeNote}\n${content}`);
  }
);

// 3c. get_graph_spec
server.tool(
  "get_graph_spec",
  "Get the NodeGraph JSON format specification (schema, node/connection/fragment structure, data types, save/load rules). Call this before constructing a graph JSON for save_graph or execute_graph.",
  {},
  async () => {
    budget.consume();
    const path = join(mcpDocsDir, "graph-spec-reference.md");
    const content = existsSync(path)
      ? readFileSync(path, "utf-8")
      : "(graph-spec-reference.md not found)";
    return respond(content);
  }
);

// 3d. get_webui_plugin_guide
server.tool(
  "get_webui_plugin_guide",
  "Get the WebUI extension plugin reference (widget/nodeRenderer/panel/node-type-override/menu/context-menu/overlay/event-hook APIs via window.NGOL). Call this before writing any .js file under WebUI/plugins/. IMPORTANT: read the warning at the top of this guide before starting — WebUI plugin tasks must be implemented as third-party .js files only, never by modifying NodeGraphModLab core.",
  {},
  async () => {
    budget.consume();
    const path = join(mcpDocsDir, "webui-plugin-reference.md");
    const content = existsSync(path)
      ? readFileSync(path, "utf-8")
      : "(webui-plugin-reference.md not found)";
    return respond(content);
  }
);

// 4. list_graphs
server.tool(
  "list_graphs",
  "List all saved graphs.",
  {},
  async () => {
    return call(async () => {
      const resp = await client.sendAndWait(
        { type: "list_graphs" },
        "list_graphs_response"
      );
      return respond(JSON.stringify(resp["graphs"], null, 2));
    });
  }
);

// 5. load_graph
server.tool(
  "load_graph",
  "Load a saved graph by name or id and return its JSON.",
  {
    nameOrId: z.string().describe("Graph name or id to load"),
  },
  async ({ nameOrId }) => {
    return call(async () => {
      // C# handler expects "id" key; name-based resolution is done client-side
      let id = nameOrId;
      const listResp = await client.sendAndWait({ type: "list_graphs" }, "list_graphs_response");
      const graphs = listResp["graphs"] as Array<{ id: string; name: string }> | undefined;
      if (graphs) {
        const byName = graphs.find((g) => g.name === nameOrId);
        if (byName) id = byName.id;
      }
      const resp = await client.sendAndWait(
        { type: "load_graph", id },
        "load_graph_response"
      );
      return respond(JSON.stringify(resp["graph"], null, 2));
    });
  }
);

// 5b. open_graph_in_browser
server.tool(
  "open_graph_in_browser",
  "Tell the most-recently-connected NodeGraphModLab WebUI browser tab to load the given saved graph via a WebSocket push — no URL/query-string manipulation involved. Does not open a browser tab itself; open http://localhost:11156/ separately first (e.g. via shell start command) if no tab is open yet. If the response's delivered field is false, no browser tab was connected to push to.",
  {
    nameOrId: z.string().describe("Graph name or id to open"),
  },
  async ({ nameOrId }) => {
    return call(async () => {
      let id = nameOrId;
      const listResp = await client.sendAndWait({ type: "list_graphs" }, "list_graphs_response");
      const graphs = listResp["graphs"] as Array<{ id: string; name: string }> | undefined;
      if (graphs) {
        const byName = graphs.find((g) => g.name === nameOrId);
        if (byName) id = byName.id;
      }
      const resp = await client.sendAndWait(
        { type: "open_graph", id },
        "open_graph_response"
      );
      return respond(JSON.stringify(resp, null, 2));
    });
  }
);

// 6. save_graph
server.tool(
  "save_graph",
  "Save a NodeGraph JSON object to persistent storage. For the required JSON format (schemaVersion, nodes, connections, fragments, fragmentLinks, groups, annotations fields), call get_graph_spec first. " +
  "IMPORTANT: Always set each node's position { x, y } (e.g. x: 50, 350, 700, ... spaced 300px apart). Without positions, all nodes overlap in the WebUI.",
  {
    graph: z.record(z.string(), z.unknown()).describe("NodeGraph JSON object to save"),
  },
  async ({ graph }) => {
    return call(async () => {
      const resp = await client.sendAndWait(
        { type: "save_graph", graph },
        "save_graph_response"
      );
      const text = resp["success"]
        ? `Saved: ${resp["id"]}`
        : `Save failed: ${resp["errorMessage"] ?? "unknown error"}`;
      return respond(text);
    });
  }
);

// 6b. save_and_open_graph_file
server.tool(
  "save_and_open_graph_file",
  "Read a NodeGraph JSON file from a local path (e.g. a graph committed inside the project source tree), save it to persistent storage (same as save_graph), then push it to the most-recently-connected WebUI browser tab via WebSocket (same as open_graph_in_browser). Combines both steps into one call. Does not open a browser tab itself; open the WebUI URL separately first if no tab is open yet.",
  {
    filePath: z.string().describe("Absolute or relative (to MCP server cwd) path to a NodeGraph JSON file"),
  },
  async ({ filePath }) => {
    return call(async () => {
      const resolvedPath = isAbsolute(filePath) ? filePath : resolve(filePath);
      if (!existsSync(resolvedPath)) {
        return respond(`File not found: ${resolvedPath}`);
      }
      let graph: Record<string, unknown>;
      try {
        let raw = readFileSync(resolvedPath, "utf-8");
        if (raw.charCodeAt(0) === 0xfeff) raw = raw.slice(1); // strip UTF-8 BOM
        graph = JSON.parse(raw);
      } catch (err) {
        return respond(`Failed to parse JSON: ${(err as Error).message}`);
      }

      const saveResp = await client.sendAndWait(
        { type: "save_graph", graph },
        "save_graph_response"
      );
      if (!saveResp["success"]) {
        return respond(`Save failed: ${saveResp["errorMessage"] ?? "unknown error"}`);
      }
      const id = saveResp["id"] as string;

      const openResp = await client.sendAndWait(
        { type: "open_graph", id },
        "open_graph_response"
      );
      return respond(`Saved: ${id}\n${JSON.stringify(openResp, null, 2)}`);
    });
  }
);

// 7. execute_graph
server.tool(
  "execute_graph",
  "Execute a NodeGraph and return execution logs and snapshots. For the required JSON format, call get_graph_spec first. For analysis graphs that log JSON:{...}, check the Logs output. For Snapshot nodes, check the Snapshots output.",
  {
    graph: z.record(z.string(), z.unknown()).describe("NodeGraph JSON object to execute"),
  },
  async ({ graph }) => {
    return call(async () => {
      const { result, logs, snapshots } = await client.sendAndWaitExecution(
        { type: "execute_graph", graph },
        30000
      );
      const lines: string[] = [
        result["success"]
          ? `Execution succeeded: ${(result["durationMs"] as number | undefined)?.toFixed(1) ?? "?"}ms`
          : `Execution failed: ${result["errorMessage"] ?? "unknown error"}`,
      ];
      if (logs.length > 0) {
        lines.push("Logs:\n" + logs.map((l) => `  ${l}`).join("\n"));
      }
      if (Object.keys(snapshots).length > 0) {
        lines.push(
          "Snapshots:\n" +
            Object.entries(snapshots)
              .map(([k, v]) => `  ${k}: ${v}`)
              .join("\n")
        );
      }
      if (!result["success"]) {
        const hostLog = readHostLog(30);
        if (hostLog) lines.push("\n--- Host Log (tail 30) ---\n" + hostLog);
      }
      if (result["success"]) lines.push(SAVE_PROPOSAL);
      return respond(lines.join("\n"), "execute_graph");
    });
  }
);

// 8. execute_all_fragments
server.tool(
  "execute_all_fragments",
  "Execute all fragments of a graph in dependency order, optionally with pinned snapshot nodes. Use this for multi-fragment graphs that use fragmentLinks. For the JSON format, call get_graph_spec first.",
  {
    graph: z.record(z.string(), z.unknown()).describe("NodeGraph JSON object"),
    pinnedNodeIds: z
      .array(z.string())
      .optional()
      .describe("Node IDs to pin (snapshot preserved across fragments)"),
  },
  async ({ graph, pinnedNodeIds }) => {
    return call(async () => {
      const { result, logs, snapshots } = await client.sendAndWaitExecution(
        { type: "execute_all_fragments", graph, pinnedNodeIds: pinnedNodeIds ?? [] },
        30000
      );
      const lines: string[] = [
        result["success"]
          ? `All fragments executed successfully: ${(result["durationMs"] as number | undefined)?.toFixed(1) ?? "?"}ms`
          : `All-fragments execution failed: ${result["errorMessage"] ?? "unknown error"}`,
      ];
      if (logs.length > 0) {
        lines.push("Logs:\n" + logs.map((l) => `  ${l}`).join("\n"));
      }
      if (Object.keys(snapshots).length > 0) {
        lines.push(
          "Snapshots:\n" +
            Object.entries(snapshots)
              .map(([k, v]) => `  ${k}: ${v}`)
              .join("\n")
        );
      }
      if (!result["success"]) {
        const hostLog = readHostLog(30);
        if (hostLog) lines.push("\n--- Host Log (tail 30) ---\n" + hostLog);
      }
      if (result["success"]) lines.push(SAVE_PROPOSAL);
      return respond(lines.join("\n"), "execute_all_fragments");
    });
  }
);

// 8b. execute_fragment
server.tool(
  "execute_fragment",
  "Execute a specific fragment of a NodeGraph by fragment ID. " +
  "Upstream fragments (those linked via fragmentLinks) are executed automatically first. " +
  "Snapshots persist across execute_fragment calls within the same session. " +
  "The graph MUST have explicit 'fragments' array defined. " +
  "For the JSON format, call get_graph_spec first.",
  {
    graph: z.record(z.string(), z.unknown()).describe(
      "NodeGraph JSON object. Must have 'fragments' array with each fragment's nodeInstanceIds."
    ),
    fragmentId: z.string().describe(
      "ID of the fragment to execute (must match an id in graph.fragments)"
    ),
    pinnedFragmentIds: z
      .array(z.string())
      .optional()
      .describe("Fragment IDs to skip; their snapshots are used as-is from the session store"),
  },
  async ({ graph, fragmentId, pinnedFragmentIds }) => {
    return call(async () => {
      const { result, logs, snapshots } = await client.sendAndWaitExecution(
        { type: "execute_fragment", graph, fragmentId, pinnedFragmentIds: pinnedFragmentIds ?? [] },
        30000
      );
      const lines: string[] = [
        result["success"]
          ? `Fragment execution succeeded [${fragmentId}]: ${(result["durationMs"] as number | undefined)?.toFixed(1) ?? "?"}ms`
          : `Fragment execution failed [${fragmentId}]: ${result["errorMessage"] ?? "unknown error"}`,
      ];
      if (logs.length > 0) {
        lines.push("Logs:\n" + logs.map((l: string) => `  ${l}`).join("\n"));
      }
      if (Object.keys(snapshots).length > 0) {
        lines.push(
          "Snapshots:\n" +
            Object.entries(snapshots)
              .map(([k, v]) => `  ${k}: ${v}`)
              .join("\n")
        );
      }
      if (!result["success"]) {
        const hostLog = readHostLog(30);
        if (hostLog) lines.push("\n--- Host Log (tail 30) ---\n" + hostLog);
      }
      return respond(lines.join("\n"), "execute_fragment");
    });
  }
);

// 9. run_node
server.tool(
  "run_node",
  "Execute a single node directly without building a graph. " +
  "Returns primitive output values inline and wraps reference-type outputs (e.g. GameObject) as $snapshot handles. " +
  "$snapshot handles can be passed as inputs to subsequent run_node calls, enabling multi-step workflows without graph JSON. " +
  "IMPORTANT: Do NOT call release_snapshot until the entire workflow is complete. " +
  "Releasing a $snapshot handle mid-workflow will destroy the reference and break downstream nodes. " +
  "Only release handles after all processing that depends on them is finished.",
  {
    nodeTypeId: z.string().describe(
      "Node type ID to execute (use get_available_nodes to find valid IDs)"
    ),
    inputs: z
      .record(z.string(), z.unknown())
      .optional()
      .describe(
        "Input port values. Primitives (number, string, boolean, null) are passed directly. " +
        "Reference-type values from a previous run_node call must be passed as {\"$snapshot\": \"<handle>\"} objects."
      ),
  },
  async ({ nodeTypeId, inputs }) => {
    return call(async () => {
      const resp = await client.sendAndWait(
        { type: "execute_node", nodeTypeId, inputs: inputs ?? {} },
        "execute_node_response",
        15000
      );
      const success = resp["success"] as boolean;
      const durationMs = (resp["durationMs"] as number | undefined)?.toFixed(1) ?? "?";
      const outputs = resp["outputs"] as Record<string, unknown> | undefined ?? {};
      const logs = resp["logs"] as string[] | undefined ?? [];

      const lines: string[] = [
        success
          ? `run_node success [${nodeTypeId}]: ${durationMs}ms`
          : `run_node failed [${nodeTypeId}]: ${resp["errorMessage"] ?? "unknown error"}`,
      ];

      if (Object.keys(outputs).length > 0) {
        lines.push(
          "Outputs:\n" +
            Object.entries(outputs)
              .map(([k, v]) =>
                typeof v === "object" && v !== null && "$snapshot" in v
                  ? `  ${k}: $snapshot="${(v as Record<string, unknown>)["$snapshot"]}"`
                  : `  ${k}: ${JSON.stringify(v)}`
              )
              .join("\n")
        );
      }
      if (logs.length > 0) {
        lines.push("Logs:\n" + logs.map((l: string) => `  ${l}`).join("\n"));
      }
      if (!success) {
        const hostLog = readHostLog(30);
        if (hostLog) lines.push("\n--- Host Log (tail 30) ---\n" + hostLog);
      }
      return respond(lines.join("\n"), "run_node");
    });
  }
);

// 9b. release_snapshot
server.tool(
  "release_snapshot",
  "Release a $snapshot handle from the session's SnapshotStore to free the referenced object from memory. " +
  "Only call this after the entire workflow that uses the snapshot is complete. " +
  "Releasing a handle mid-workflow will destroy the in-process reference and cause downstream run_node calls to receive null.",
  {
    key: z.string().describe(
      "The snapshot handle key in the format \"nodeInstanceId:portName\" " +
      "(the value after the $snapshot field returned by run_node)"
    ),
  },
  async ({ key }) => {
    return call(async () => {
      const resp = await client.sendAndWait(
        { type: "release_snapshot", key },
        "release_snapshot_response",
        10000
      );
      const released = resp["released"] as boolean | undefined;
      return respond(released ? `Released: ${key}` : `Not found (already released?): ${key}`);
    });
  }
);

// 10. save_node_source
server.tool(
  "save_node_source",
  "Save a C# node source file for hot-reload (no game restart needed). Waits for compile result (up to 6s) and returns success/failure. " +
  "Use folder to control where the file goes: 'ai_generated' (default, permanent) or a custom subfolder (e.g. 'experiments'). " +
  "Requires the game to be running and NGOL_SCRIPTS_DIR env var to be set (or auto-detected via welcome message).",
  {
    fileName: z.string().describe("File name (e.g. MyNode.cs). Must end with .cs"),
    source: z.string().describe("C# source code to save"),
    folder: z.string().optional().describe(
      "Subfolder under NGOL_SCRIPTS_DIR (e.g. 'experiments'). Default: 'ai_generated'."
    ),
  },
  async ({ fileName, source, folder }) => {
    budget.consume();
    await ensureConnected();
    const scriptsDir = getScriptsDir();
    if (!scriptsDir) {
      return respond("Error: scriptsDir not resolved. Set NGOL_SCRIPTS_DIR in .mcp.json env, or connect to the game first.", "save_node_source");
    }
    if (!fileName.endsWith(".cs")) {
      return respond(`Error: fileName must end with .cs (got: ${fileName})`, "save_node_source");
    }
    try {
      const targetDir = join(scriptsDir, folder ?? "ai_generated");
      mkdirSync(targetDir, { recursive: true });
      const filePath = join(targetDir, fileName);

      // プッシュ待機をファイル書き込み前に登録（競合防止: 書き込み後だとホスト側が先送信する可能性）
      const pushPromise = client.waitForPush(
        ["node_list_updated", "script_compile_error"],
        6000  // debounce 500ms + コンパイル時間 + バッファ
      );

      writeFileSync(filePath, source, "utf-8");

      try {
        const pushMsg = await pushPromise;
        const msgType = pushMsg["type"] as string;
        if (msgType === "node_list_updated") {
          const ids = (pushMsg["updatedNodeTypeIds"] as string[] | undefined) ?? [];
          nodeCache = null;  // ノードキャッシュを無効化
          return respond(
            `Saved & compiled: ${filePath}\n` +
            `Registered nodes: ${ids.length > 0 ? ids.join(", ") : "(none)"}`,
            "save_node_source"
          );
        } else {
          // script_compile_error — diagnostics があれば詳細表示、なければホストログで補完
          const errMsg = (pushMsg["errorMessage"] as string | undefined) ?? "unknown error";
          const diags = (pushMsg["diagnostics"] as string[] | undefined) ?? [];
          const diagText = diags.length > 0
            ? "\nDiagnostics:\n" + diags.map(d => `  ${d}`).join("\n")
            : "";
          const hostLog = diags.length === 0 ? readHostLog(20) : null;
          return respond(
            `Saved but compile FAILED: ${filePath}\n` +
            `Error: ${errMsg}${diagText}` +
            (hostLog ? `\n--- Host Log (last 20 lines) ---\n${hostLog}` : ""),
            "save_node_source"
          );
        }
      } catch {
        // タイムアウト（ゲーム未起動や FileSystemWatcher 異常）
        return respond(
          `Saved: ${filePath}\n` +
          `Warning: Compile confirmation timed out (6s). Check host log for status.`,
          "save_node_source"
        );
      }
    } catch (err) {
      return respond(`Error writing file: ${(err as Error).message}`, "save_node_source");
    }
  }
);

// 11. delete_graph
server.tool(
  "delete_graph",
  "Delete a saved graph by name or id.",
  {
    nameOrId: z.string().describe("Graph name or id to delete"),
  },
  async ({ nameOrId }) => {
    return call(async () => {
      // C# handler expects "id" key; name-based resolution is done client-side
      let id = nameOrId;
      const listResp = await client.sendAndWait({ type: "list_graphs" }, "list_graphs_response");
      const graphs = listResp["graphs"] as Array<{ id: string; name: string }> | undefined;
      if (graphs) {
        const byName = graphs.find((g) => g.name === nameOrId);
        if (byName) id = byName.id;
      }
      const resp = await client.sendAndWait(
        { type: "delete_graph", id },
        "delete_graph_response"
      );
      const text = resp["success"]
        ? `Deleted: ${nameOrId}`
        : `Delete failed: ${resp["errorMessage"] ?? "unknown error"}`;
      return respond(text);
    });
  }
);

// 12. list_persistent_nodes
server.tool(
  "list_persistent_nodes",
  "List all currently active persistent node registrations (nodes running every frame). Use this to find nodeInstanceIds before stopping specific nodes.",
  {},
  async () => {
    return call(async () => {
      const resp = await client.sendAndWait(
        { type: "list_persistent_nodes" },
        "list_persistent_nodes_response"
      );
      const nodes = resp["nodes"] as Array<{ nodeInstanceId: string; displayName: string; graphName: string }> | undefined ?? [];
      if (nodes.length === 0) return respond("Active persistent nodes: (none)");
      const lines = ["Active persistent nodes:", ...nodes.map(n => `  [${n.nodeInstanceId}] ${n.displayName} (graph: ${n.graphName})`)];
      return respond(lines.join("\n"));
    });
  }
);

// 13. stop_persistent
server.tool(
  "stop_persistent",
  "Stop ALL active persistent node registrations (cancel all per-frame callbacks). Use this to reset the game state before re-running a modified persistent node.",
  {},
  async () => {
    return call(async () => {
      const resp = await client.sendAndWait(
        { type: "stop_persistent" },
        "stop_persistent_response"
      );
      const count = (resp["stoppedCount"] as number | undefined) ?? 0;
      return respond(`Stopped ${count} persistent node registration(s).`);
    });
  }
);

// 14. stop_persistent_node
server.tool(
  "stop_persistent_node",
  "Stop a specific persistent node by its nodeInstanceId. Use list_persistent_nodes first to get the ID.",
  {
    nodeInstanceId: z.string().describe("The nodeInstanceId of the persistent node to stop"),
  },
  async ({ nodeInstanceId }) => {
    return call(async () => {
      const resp = await client.sendAndWait(
        { type: "stop_persistent_node", nodeInstanceId },
        "stop_persistent_node_response"
      );
      const found = resp["found"] as boolean | undefined;
      return respond(found
        ? `Stopped: ${nodeInstanceId}`
        : `Not found (already stopped?): ${nodeInstanceId}`);
    });
  }
);

// 15. get_budget_status
server.tool(
  "get_budget_status",
  "Check remaining tool call budget for this session.",
  {},
  async () => {
    return { content: [{ type: "text" as const, text: budget.status() }] };
  }
);

// 16. get_browser_debug_log
server.tool(
  "get_browser_debug_log",
  "Retrieve recent browser console and DOM debug log entries captured by WebUI Debug Bridge (Debug > Debug Bridge must be ON). " +
    "Use kind/level/messageContains to narrow results — dom_event payloads can be large, so filter before widening count. " +
    "domEventType matches the JSON 'type' field of dom_event payloads exactly (stricter than messageContains, which can false-match on unrelated substrings). " +
    "levelAtLeast returns entries at or above a severity (log < warn < error). " +
    "sinceMs/untilMs filter by epoch-millisecond timestamp range. " +
    "messageRegex applies a case-insensitive .NET regex (200ms match timeout; invalid patterns match nothing rather than erroring). " +
    "Example: { kind: 'dom_event', domEventType: 'contextmenu', count: 5 }.",
  {
    count: z.number().optional().describe("Max matching entries to return (default 10)"),
    kind: z.enum(["console", "dom_event"]).optional().describe("Filter by entry kind"),
    level: z.enum(["log", "warn", "error"]).optional().describe("Filter by exact log level"),
    messageContains: z.string().optional().describe("Substring match (case-insensitive) on the message field"),
    domEventType: z.string().optional().describe("Exact match on dom_event JSON 'type' field (e.g. 'contextmenu', 'mousedown'). Implies kind=dom_event."),
    sinceMs: z.number().optional().describe("Only entries at or after this epoch-millisecond timestamp"),
    untilMs: z.number().optional().describe("Only entries at or before this epoch-millisecond timestamp"),
    levelAtLeast: z.enum(["log", "warn", "error"]).optional().describe("Only entries at or above this severity"),
    messageRegex: z.string().optional().describe("Case-insensitive .NET regex match on the message field (200ms timeout)"),
  },
  async ({ count, kind, level, messageContains, domEventType, sinceMs, untilMs, levelAtLeast, messageRegex }) => {
    return call(async () => {
      const resp = await client.sendAndWait(
        {
          type: "get_debug_log",
          count: count ?? 10,
          kind,
          level,
          messageContains,
          domEventType,
          sinceMs,
          untilMs,
          levelAtLeast,
          messageRegex,
        },
        "get_debug_log_response"
      );
      const entries = (resp["entries"] as Array<Record<string, unknown>> | undefined) ?? [];
      if (entries.length === 0) {
        return respond("No debug log entries matched. Enable Debug > Debug Bridge in WebUI, reproduce the issue, or loosen the filter.");
      }
      const lines = entries.map(e =>
        `[${e["timestampMs"]}] [${e["kind"]}/${e["level"]}] ${e["message"]}`
      );
      return respond(lines.join("\n"));
    });
  }
);

// 17. get_connection_info
server.tool(
  "get_connection_info",
  "Report which NGOL process this MCP server is currently connected to: gameName / runtimeType / pluginDir / port. " +
  "Lightweight — returns cached welcome-message data, no extra round trip. " +
  "Call this at the start of an investigation, or whenever a result looks unexpected, in environments where multiple NGOL-embedded processes may listen on the same default port. " +
  "Other tools (get_node_detail/search_nodes/run_node/execute_graph/...) do not show this info automatically — only get_available_nodes does.",
  {},
  async () => {
    return call(async () => {
      const gameName = client.detectedGameName ?? "(unknown)";
      const rt = client.detectedRuntimeType ?? "(unknown)";
      const pluginDir = client.detectedPluginDir ?? "(unknown)";
      let port = "(unknown)";
      try {
        port = new URL(client.url).port || port;
      } catch {
        /* ignore */
      }
      return respond(
        `Game: ${gameName}\n` +
        `Runtime: ${rt}\n` +
        `Plugin dir: ${pluginDir}\n` +
        `Port: ${port}`
      );
    });
  }
);

const transport = new StdioServerTransport();
await server.connect(transport);
