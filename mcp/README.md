# NodeGraphModLab MCP サーバーガイド

**対象読者**: Claude Code・VS Code Copilot 等、MCP 対応 AI エージェントを使うユーザー

---

## 概要

NodeGraphModLab MCP サーバーは、AI エージェントがホストプロセスに組み込まれた NodeGraphModLab (NGOL) と対話するためのブリッジです。
Claude Code 専用ではなく、MCP プロトコルに対応した任意のエージェントから利用できます。

```
AI エージェント
    ↕ MCP (stdio)
ngol-mcp-server (Node.js)
    ↕ WebSocket ws://127.0.0.1:11156/ws
NodeGraphModLab.Core（ホストプロセスに組み込まれた .NET ランタイム）
    ↕ Roslyn コンパイラ / グラフ実行エンジン
ホストプロセス（自作 .NET アプリケーション等）
```

---

## セットアップ

### 環境変数

| 変数名 | 必須 | デフォルト | 説明 |
|--------|------|-----------|------|
| `NGOL_WS_URL` | 任意 | `ws://127.0.0.1:11156/ws` | NGOL の WebSocket URL |
| `NGOL_SCRIPTS_DIR` | 任意 | （自動） | カスタムノード `.cs` の保存先。未設定時はホストの `ngolRoot/Nodes/CustomNodes/cs` を自動使用 |
| `NGOL_MAX_RESPONSE_CHARS` | 任意 | `8000` | ツールレスポンスの最大文字数。大きな解析結果を取得する場合は増やす |
| `NGOL_MAX_TOOL_CALLS` | 任意 | `100` | 1 セッションで呼び出せるツール数の上限 |
| `NGOL_REMINDERS_FILE` | 任意 | （自動検索） | リマインダー設定 JSON の絶対パス |
| `NGOL_DOCS_DIR` | 任意 | 同梱の `docs/` | ガイド文書（`node-dev-reference.md` 等）を差し替えるディレクトリの絶対パス。配布物を書き換えずに独自ガイドへ差し替えたい場合に使う |

### `.mcp.json` 設定例（Claude Code）

`mcp/mcp.json.example` をコピーし、パスを自分の `ngolRoot`（NGOL ランタイム一式を配置したフォルダ）に合わせて調整してください。

```json
{
  "mcpServers": {
    "ngol": {
      "type": "stdio",
      "command": "node",
      "args": ["<ngolRoot>/mcp/dist/index.js"],
      "env": {
        "NGOL_WS_URL": "ws://127.0.0.1:11156/ws",
        "NGOL_SCRIPTS_DIR": "./Nodes/CustomNodes/cs",
        "NGOL_MAX_RESPONSE_CHARS": "12000"
      }
    }
  }
}
```

> ホストプロセスが起動し WebSocket サーバー（:11156）が listen している状態で使用してください。

### リマインダー機能（任意）

MCP ツールのレスポンス末尾に任意のメッセージを挿入する機能です。AI エージェントへの
重要事項リマインドに使えます。

**設定ファイル**: `mcp/ngol-reminders.json`（`mcp/reminders.json.example` をコピーして作成）

```json
{
  "reminders": [
    { "text": "Consider verifying the result with the WebUI.", "mode": "random", "probability": 0.4 },
    { "text": "If you found something new, update the relevant document.", "mode": "interval", "intervalCalls": 5 },
    { "text": "Always verify the host log after hot-reload.", "mode": "always" }
  ],
  "targetTools": ["run_node", "execute_graph", "execute_all_fragments", "execute_fragment", "compile_node", "save_node_source"],
  "header": "📌 Reminder"
}
```

| mode | 説明 | 追加パラメータ |
|------|------|---------------|
| `always` | 毎回表示 | なし |
| `random` | 指定確率で表示 | `probability`: 0.0〜1.0 |
| `interval` | N 回に 1 回表示 | `intervalCalls`: 整数 |

設定ファイルが存在しない場合はリマインダー機能は無効です。

---

## ツールリファレンス（26 ツール）

1 セッションで呼び出せるツール数は `NGOL_MAX_TOOL_CALLS`（デフォルト 100）に制限されます
（`get_budget_status` を除く）。

### 情報取得系

| ツール | 内容 |
|---|---|
| `get_available_nodes` / `search_nodes` / `get_node_detail` | 登録ノード型の一覧・検索・詳細。一覧はキャッシュされるため、まず `search_nodes` で絞り込み `get_node_detail` で詳細を取るのが効率的 |
| `get_node_dev_guide` | C# ノード開発の AI 向けリファレンス。`compile_node`/`save_node_source` の前に呼ぶこと |
| `get_graph_spec` | グラフ JSON フォーマットの AI 向けリファレンス。`save_graph`/`execute_graph` の前に呼ぶこと |
| `get_analysis_guide` | 解析ノード出力パターンとサンプルグラフ |
| `get_webui_plugin_guide` | WebUI 拡張プラグイン（widget/nodeRenderer/panel/ノード型上書き等）の AI 向けリファレンス |
| `get_budget_status` | 残りツール呼び出し数の確認（バジェット消費しない） |
| `get_connection_info` | 現在接続中のプロセスの `gameName`/`runtimeType`/`pluginDir`/`port` を返す（追加の往復なし・軽量） |
| `get_browser_debug_log` | WebUI Debug Bridge（Debug メニューで ON）が記録したブラウザ console/DOM ログを取得 |

### ノード開発系

| ツール | 内容 |
|---|---|
| `compile_node` | C# ソースをコンパイルし、ホットリロード登録。`folder` パラメータで保存先サブフォルダを指定可（省略時 `ai_generated`） |
| `save_node_source` | `.cs` ファイルをディスク保存（コンパイルはホットリロードが自動で拾う） |

### グラフ操作系

| ツール | 内容 |
|---|---|
| `list_graphs` / `load_graph` / `save_graph` / `delete_graph` | グラフの一覧・読み込み・保存・削除 |
| `open_graph_in_browser` | 保存済みグラフを、接続中の WebUI ブラウザタブへ WebSocket push で開かせる |
| `save_and_open_graph_file` | ローカルパスのグラフ JSON ファイルを保存（`save_graph` 相当）した上でブラウザに push（`open_graph_in_browser` 相当）する一括呼び出し |

### グラフ実行系

| ツール | 内容 |
|---|---|
| `execute_graph` | グラフ全体を実行し、ログとスナップショットを返す（タイムアウト 30 秒） |
| `execute_fragment` / `execute_all_fragments` | 断片単位・全断片の実行（`fragmentLinks` を使うグラフ向け） |
| `run_node` | グラフを組まずに単一ノードを直接実行 |
| `release_snapshot` | `run_node` が返す `$snapshot` ハンドルの解放 |

### 永続ノード管理系

| ツール | 内容 |
|---|---|
| `list_persistent_nodes` | 実行中の永続ノード一覧 |
| `stop_persistent_node` / `stop_persistent` | 指定ノード / 全ノードの停止 |

---

## 典型的な使用フロー

### 解析ノードを作って情報収集する

```
1. get_node_dev_guide     # API を確認
2. compile_node(          # 解析ノードをコンパイル
     source=...,
     className="MyDiagNode",
     folder="analysis"    # 任意のサブフォルダ名
   )
3. execute_graph(...)     # 1 ノードのグラフで即実行
4. Logs の JSON:{...} から結果を解析
```

### グラフを保存して WebUI で再利用する

```
1. get_graph_spec         # フォーマット確認
2. save_graph(graph=...)  # 保存
3. WebUI で開いて確認・編集
```

---

## トラブルシューティング

| 症状 | 原因 | 対処 |
|------|------|------|
| `execute_graph` が 30 秒でタイムアウト | `RegisterPersistent` を使う永続ノード | WebUI から実行しホストログで確認 |
| `compile_node` で `WARN: NGOL_SCRIPTS_DIR not set` | 環境変数未設定 | `.mcp.json` の `env` に `NGOL_SCRIPTS_DIR` を追加 |
| ホットリロードが反映されない | ホスト起動中にファイルロックが発生 | ホストプロセスを再起動して再試行 |
| `compile_node` 成功だが `get_available_nodes` に出ない | ホットリロードのコンパイルエラー | ホストログを確認（`[Scripts] Hot-reload failed`） |
| バジェット残数が少ない | 情報取得系ツールを多用している | `get_available_nodes` 等は結果をキャッシュして再呼び出しを避ける |
