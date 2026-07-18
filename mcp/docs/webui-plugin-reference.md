# NGOL WebUI プラグインリファレンス（AI 向け）

> ## ⚠️ 最重要ルール（必ず守ること）
> WebUI 拡張プラグイン（widget / nodeRenderer / panel / ノード型上書き / メニュー・
> コンテキストメニュー・オーバーレイ・イベントフック — `window.NGOL` 経由の機能全般）の
> 実装は、**`WebUI/plugins/` への `.js` 配置のみで完結させること。**
> **禁止**: 実装が難航しても、NodeGraphModLab 本体（`WebUI/src/`・`NodeGraphModLab.Core` 等）を
> 変更して `npm run build` / `dotnet build` を伴う対応に切り替えないこと。まず
> `window.NGOL` の既存 API だけで実現できないか再検討する。ユーザーが明示的に
> 「プラグイン API 自体を拡張してほしい」「本体機能として追加してほしい」と指示した
> 場合のみ本体修正に着手してよい。

他のリファレンス（解析・ノード開発・グラフ仕様）と違い、この機能は
**「人間がブラウザで見る・操作する」ことが前提の開いたループ**を扱う。
まず下の利用パターン表で「今回どちらに該当するか」を判断してから実装すること。

## 利用パターン

| パターン | 人間の関与 | 典型例 | 結果の受け取り方 |
|---|---|---|---|
| **A. 可視化補助** | 見るだけ（判断不要） | 解析結果を生 JSON より見やすい panel/widget で提示 | 不要。既存の Snapshot/Log 経路で完結する |
| **B. 対話的決定（断片）** | 操作が必須（選択・ドラッグ等） | 曖昧な候補からの選択、座標指定、キャリブレーション | `setSnapshotValue` で SnapshotStore に書き込み → `execute_fragment` / `execute_graph` で下流が読む |
| **C. 永続ノードのライブチューニング** | スライダー等の連続操作 | パラメータ調整、実行中ホストのリアルタイム変更 | `pushLiveParams(nodeId, params)` → 永続ノードが `GetLiveParam` で毎フレーム読む（Execute 不要） |

## 動作の仕組み

```
起動時: WebUI → GET /api/webui-plugins → WebUI/plugins/ のマニフェスト取得
      → 各 .js を dynamic import → JS が window.NGOL.register*() を呼ぶ
描画時: ノードの nodeTypeId / customWebUi.pluginId でレジストリを検索
      → 型ID上書き登録あり → それを最優先で使用
      → customWebUi 宣言あり → widget/nodeRenderer を使用
      → 未登録 → 標準表示（graceful degradation）
```

- プラグイン読み込み失敗・描画例外は個別に隔離され、WebUI 本体は必ず起動する
- URL に `?v=<ファイル更新時刻>` が付くため、`.js` 更新後はブラウザ **F5 だけ**で反映される

## デプロイ方法（現状の制約）

`.js`（または `plugin.json` + `index.js` フォルダ）を以下に配置し、ブラウザを F5:

```
<ngolRoot>/WebUI/plugins/
```

**現状はファイル書き込み手段（ローカル FS アクセス）が必要**。MCP プロトコルのみの
クライアント（`compile_node` のような書き込み専用ツール）は今のところ存在しない。

## window.NGOL API 早見表（apiVersion: 1）

```javascript
const {
  React, html,                                              // React 本体と共有 / htm タグ記法
  registerWidget, registerNodeRenderer, registerPanel,       // ノードに紐付く UI（宣言必須）
  registerNodeRendererOverride, registerWidgetOverride,      // 任意ノード型への直接上書き（宣言不要）
  ws,                                                        // { send, onMessage, onConnection }
  setSnapshotValue,                                          // (nodeId, portName, value) ノード実行なしでスナップショット直接書き込み
  pushLiveParams,                                            // (nodeId, params) 永続ノードへライブパラメータ部分マージ
  extensions,                                                // { registerMenu, registerNodeContextMenuItems, registerOverlay, useReactFlow }
  events,                                                    // { on(type, cb) }
  log,                                                       // console.log ラッパ
} = window.NGOL
```

| API | 用途 | 紐付け方法 |
|---|---|---|
| `registerWidget(pluginId, Component)` | ノード内ウィジェット | C# 側 `[NodeWebUi("pluginId", ...)]` 宣言が必要 |
| `registerNodeRenderer(pluginId, Component, opts?)` | フルノード描画 | 同上 |
| `registerNodeRendererOverride(nodeTypeId, Component, { label?, portLayout? })` | 任意ノード型を直接フルノード描画で上書き | **宣言不要**。ノード作者の関与なしで既存ノード（ビルトイン含む）に適用可 |
| `registerWidgetOverride(nodeTypeId, Component, { label? })` | 任意ノード型にウィジェット追加 | 同上 |
| `registerPanel({ id, title, component, defaultOpen? })` | Plugins メニューから開く独立パネル | 不要（ノードに紐付かない） |
| `extensions.registerMenu({ label, items })` | メニューバー拡張 | 不要 |
| `extensions.registerNodeContextMenuItems(fn)` | ノード右クリックメニュー拡張（`fn(ctx)` が項目配列を返す） | 不要 |
| `extensions.registerOverlay({ id, component })` | キャンバスオーバーレイ（ReactFlow 内描画） | 不要 |
| `events.on(type, cb)` | `ws_message`/`graph_loaded`/`execution_finished`/`snapshot_saved`/`selection_changed` 購読 | 不要 |
| `setSnapshotValue(nodeId, portName, value)` | UI で決定した値をノード実行なしで後続断片へ渡す（value はプリミティブのみ・PIN 中は拒否・Execute は走らない） | 不要 |
| `pushLiveParams(nodeId, params)` | 実行中の永続ノードへ `params` を部分マージ（プリミティブのみ・履歴なし）。ノードは `GetLiveParam` で opt-in 読み取り。WS 応答: `push_node_live_params_response` | 不要 |

### setSnapshotValue vs pushLiveParams（必読）

| | `setSnapshotValue` | `pushLiveParams` |
|---|---|---|
| ストア | SnapshotStore（ポート名） | LiveParamStore（任意キー） |
| 下流への伝播 | 断片リンク経由 | なし（当該永続ノードのみ） |
| ノード側 | `SetSnapshot` / 下流 `GetSnapshot` | `ctx.GetLiveParam<T>(key, default)` in `onUpdate` |
| PIN ガード | あり | なし |
| 逆方向（ノード→WebUI） | `PushLiveValue`（別チャンネル） | 未実装（将来予定） |

```javascript
// スライダー連打向け（応答は任意で購読）
NGOL.pushLiveParams(nodeId, { scale: 1.05 })
NGOL.ws.onMessage((msg) => {
  if (msg.type === 'push_node_live_params_response' && !msg.success)
    console.warn(msg.reason)  // missing_params / unsupported_type / no_valid_params
})
```

## 最小テンプレート

**widget**（`spec`/`snapshotBadge`/`snapshotBadgesByPort`/`paramValues`/`onParamChange` を受け取る）:

```javascript
const { html, registerWidget } = window.NGOL
function MyWidget({ snapshotBadgesByPort, onParamChange }) {
  return html`<div>value: ${snapshotBadgesByPort?.value?.valueString ?? '—'}</div>`
}
registerWidget('my.webui.widget', MyWidget)
```

⚠️ **複数ポートに SetSnapshot するノードは `snapshotBadgesByPort`（ポート名 → バッジ）で読むこと。**
`snapshotBadge`（単数形）は最後に SetSnapshot したポートの値しか保持しないため、
`options`→`selected` のように複数ポートへ順番に書くノードでは値が消えて見える。

**panel**（`.cs` 不要・単体で完結）:

```javascript
const { React, html, registerPanel, ws } = window.NGOL
function MyPanel({ onClose }) {
  const [items, setItems] = React.useState([])
  React.useEffect(() => ws.onMessage((msg) => {
    if (msg.type === 'snapshot_saved') setItems(prev => [msg, ...prev])
  }), [])
  return html`<div>received: ${items.length}</div>`
}
registerPanel({ id: 'my.webui.panel', title: 'My Panel', component: MyPanel })
```

**ノード型上書き**（既存ノード ID を指定するだけで上書き。`.cs` 変更不要）:

```javascript
const { html, registerNodeRendererOverride } = window.NGOL
function MyOverrideUi({ nodeTypeInfo, paramValues, onParamChange, runFragment }) {
  return html`<div>${nodeTypeInfo.displayName} (overridden)</div>`
}
registerNodeRendererOverride('ngol.logic.log', MyOverrideUi, { label: 'my-pack' })
```

- ドラッグを受ける要素には `className="nodrag"` を付ける（ノード移動と競合するため）

## ガードレール（ノード型上書き専用・常時有効）

| # | 内容 |
|---|---|
| バッジ | 上書き適用中のノードに Override バッジが表示される（title に登録元ラベル） |
| 復帰トグル | Plugins メニュー > Node Overrides でノード型単位にチェック OFF → 標準 UI に戻る |
| 失敗時フォールバック | 上書き描画が例外を投げると、そのノードインスタンスのみ標準描画へ自動復帰（エラー表示ではなく Console ログのみ） |
| 衝突検知 | 同一ノード型に複数プラグインが登録した場合は後勝ち + `console.warn`（両ラベル明記） |
