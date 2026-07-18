import { readFileSync, existsSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";

interface ReminderDef {
  text: string;
  mode: "always" | "random" | "interval";
  probability?: number;
  intervalCalls?: number;
}

interface ReminderConfig {
  reminders: ReminderDef[];
  targetTools?: string[];
  header?: string;
}

export class ReminderManager {
  private config: ReminderConfig | null = null;
  private intervalCounters: number[] = [];

  constructor(configPathEnv: string | undefined) {
    const path = this.resolvePath(configPathEnv);
    if (!path) return;
    try {
      let raw = readFileSync(path, "utf-8");
      if (raw.charCodeAt(0) === 0xfeff) raw = raw.slice(1); // strip UTF-8 BOM
      this.config = JSON.parse(raw) as ReminderConfig;
      this.intervalCounters = new Array(this.config.reminders.length).fill(0);
    } catch {
      // 設定ファイルが読めない場合は無効化（既存動作と同一）
    }
  }

  private resolvePath(envPath: string | undefined): string | null {
    if (envPath) return envPath;
    // MCP サーバー起動ディレクトリ（dist/の親 = mcp/）を自動検索
    const mcpDir = join(dirname(fileURLToPath(import.meta.url)), "..");
    const candidate = join(mcpDir, "ngol-reminders.json");
    return existsSync(candidate) ? candidate : null;
  }

  /** 表示するリマインダーテキストを返す。条件に合わなければ null */
  pick(toolName: string): string | null {
    if (!this.config) return null;

    const { reminders, targetTools, header } = this.config;

    if (targetTools && targetTools.length > 0 && !targetTools.includes(toolName)) {
      return null;
    }

    const selected: string[] = [];

    for (let i = 0; i < reminders.length; i++) {
      const r = reminders[i];
      let show = false;

      if (r.mode === "always") {
        show = true;
      } else if (r.mode === "random") {
        const prob = r.probability ?? 0.5;
        show = Math.random() < prob;
      } else if (r.mode === "interval") {
        const n = r.intervalCalls ?? 1;
        this.intervalCounters[i]++;
        show = this.intervalCounters[i] % n === 0;
      }

      if (show) selected.push(r.text);
    }

    if (selected.length === 0) return null;

    const h = header ?? "📌 Reminder";
    return `${h}\n` + selected.map(t => `• ${t}`).join("\n");
  }
}
