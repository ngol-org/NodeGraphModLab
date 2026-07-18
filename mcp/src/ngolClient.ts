import WebSocket from "ws";

interface PendingReq {
  resolve: (value: unknown) => void;
  reject: (reason: unknown) => void;
  timer: ReturnType<typeof setTimeout>;
  responseType: string;
}

interface PendingPush {
  types: string[];   // 待機するメッセージタイプ（複数可）
  resolve: (msg: Record<string, unknown>) => void;
  reject: (reason: unknown) => void;
  timer: ReturnType<typeof setTimeout>;
}

export interface ExecResult {
  result: Record<string, unknown>;
  logs: string[];
  snapshots: Record<string, string>;
}

export class NgolClient {
  private ws: WebSocket | null = null;
  private pending = new Map<string, PendingReq>();
  private pushListeners: PendingPush[] = [];
  private execPending: {
    resolve: (value: ExecResult) => void;
    reject: (reason: unknown) => void;
    timer: ReturnType<typeof setTimeout>;
    logs: string[];
    snapshots: Record<string, string>;
  } | null = null;

  /** welcome メッセージから取得したプラグイン配置フォルダ */
  detectedPluginDir: string | undefined;
  /** welcome メッセージから取得したランタイム種別 */
  detectedRuntimeType: "IL2CPP" | "Mono" | undefined;
  /** welcome メッセージから取得したゲーム名 */
  detectedGameName: string | undefined;

  constructor(public readonly url: string) {}

  async connect(): Promise<void> {
    if (this.ws?.readyState === WebSocket.OPEN) return;
    return new Promise((resolve, reject) => {
      const wsUrl = this.url.includes("?") ? `${this.url}&client=mcp` : `${this.url}?client=mcp`;
      const token = process.env.NGOL_AUTH_TOKEN;
      const ws = token ? new WebSocket(wsUrl, [token]) : new WebSocket(wsUrl);
      let welcomed = false;
      const onWelcome = () => { if (!welcomed) { welcomed = true; resolve(); } };
      // welcome が来なくても open から 3 秒後には接続完了とみなす
      ws.on("open", () => {
        this.ws = ws;
        setTimeout(onWelcome, 3000);
      });
      ws.on("error", reject);
      ws.on("message", (raw: WebSocket.RawData) => {
        const s = String(raw);
        // welcome メッセージを先取りして detectedScriptsDir を設定
        try {
          const msg = JSON.parse(s) as Record<string, unknown>;
          if (msg["type"] === "welcome") {
            const dir = msg["pluginDir"] as string | undefined;
            if (dir) this.detectedPluginDir = dir;
            const rt = msg["runtimeType"] as string | undefined;
            if (rt === "IL2CPP" || rt === "Mono") this.detectedRuntimeType = rt;
            const gn = msg["gameName"] as string | undefined;
            if (gn) this.detectedGameName = gn;
            onWelcome();
          }
        } catch { /* ignore */ }
        this.onMessage(s);
      });
      ws.on("close", () => { this.ws = null; });
    });
  }

  isConnected(): boolean {
    return this.ws?.readyState === WebSocket.OPEN;
  }

  disconnect(): void {
    this.ws?.close();
    this.ws = null;
  }

  async sendAndWait(
    req: Record<string, unknown>,
    responseType: string,
    timeoutMs = 15000
  ): Promise<Record<string, unknown>> {
    const reqId = `${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
    const payload = { ...req, requestId: reqId };
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(reqId);
        reject(new Error(`Timeout waiting for ${responseType} (${timeoutMs}ms)`));
      }, timeoutMs);
      this.pending.set(reqId, {
        resolve: (v) => resolve(v as Record<string, unknown>),
        reject,
        timer,
        responseType,
      });
      this.ws!.send(JSON.stringify(payload));
    });
  }

  /**
   * 指定タイプのプッシュメッセージを一度だけ待機する。
   * node_list_updated / script_compile_error など requestId なしのブロードキャストに使用。
   * ファイル書き込み前にリスナーを登録することで競合を防ぐ。
   */
  async waitForPush(
    types: string[],
    timeoutMs = 5000
  ): Promise<Record<string, unknown>> {
    return new Promise((resolve, reject) => {
      const listener: PendingPush = {
        types,
        resolve,
        reject,
        timer: setTimeout(() => {
          this.pushListeners = this.pushListeners.filter(l => l !== listener);
          reject(new Error(`Timeout waiting for push: [${types.join(", ")}] (${timeoutMs}ms)`));
        }, timeoutMs),
      };
      this.pushListeners.push(listener);
    });
  }

  async sendAndWaitExecution(
    req: Record<string, unknown>,
    timeoutMs = 30000
  ): Promise<ExecResult> {
    return new Promise((resolve, reject) => {
      const logs: string[] = [];
      const snapshots: Record<string, string> = {};
      const timer = setTimeout(() => {
        this.execPending = null;
        reject(new Error(`Timeout waiting for execution_result (${timeoutMs}ms)`));
      }, timeoutMs);
      this.execPending = {
        resolve,
        reject,
        timer,
        logs,
        snapshots,
      };
      this.ws!.send(JSON.stringify(req));
    });
  }

  private onMessage(raw: string): void {
    let msg: Record<string, unknown>;
    try {
      msg = JSON.parse(raw) as Record<string, unknown>;
    } catch {
      return;
    }

    const type = msg["type"] as string | undefined;

    // execution_log / snapshot_saved は execPending に蓄積
    if (type === "execution_log" && this.execPending) {
      const level = (msg["level"] as string | undefined) ?? "info";
      const message = (msg["message"] as string | undefined) ?? "";
      this.execPending.logs.push(`[${level}] ${message}`);
      return;
    }
    if (type === "snapshot_saved" && this.execPending) {
      const portName = (msg["portName"] as string | undefined) ?? "unknown";
      const valueString = (msg["valueString"] as string | undefined) ?? "";
      this.execPending.snapshots[portName] = valueString;
      return;
    }
    if (type === "execution_result" && this.execPending) {
      const ep = this.execPending;
      this.execPending = null;
      clearTimeout(ep.timer);
      ep.resolve({ result: msg, logs: ep.logs, snapshots: ep.snapshots });
      return;
    }

    // プッシュリスナーへのディスパッチ（ブロードキャスト待機: node_list_updated / script_compile_error など）
    if (type) {
      const matchIdx = this.pushListeners.findIndex(l => l.types.includes(type));
      if (matchIdx !== -1) {
        const [listener] = this.pushListeners.splice(matchIdx, 1);
        clearTimeout(listener.timer);
        listener.resolve(msg);
        return;
      }
    }

    // 通常の request/response マッチング
    // 1. requestId が返ってきた場合は完全一致
    const reqId = msg["requestId"] as string | undefined;
    if (reqId && this.pending.has(reqId)) {
      const p = this.pending.get(reqId)!;
      this.pending.delete(reqId);
      clearTimeout(p.timer);
      p.resolve(msg);
      return;
    }
    // 2. requestId なし（C# ハンドラーが未対応）の場合は responseType でフォールバックマッチング
    if (type) {
      for (const [id, p] of this.pending) {
        if (p.responseType === type) {
          this.pending.delete(id);
          clearTimeout(p.timer);
          p.resolve(msg);
          return;
        }
      }
    }
  }
}
