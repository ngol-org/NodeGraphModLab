# NodeGraphModLab — WebUI プラグインガイド

WebUI を再ビルドせずに、`.js` ファイルの配置だけで WebUI に UI を追加できる
**外部 UI プラグインシステム**のガイドです。

対象読者:
- **利用ユーザー**: 配布されたプラグインをインストールする人 → §2 だけ読めば OK
- **プラグイン作者**: JS で UI を作る人 → §3 以降
- **ノード開発者**: 自作ノードにカスタム UI を付けたい人 → §4 と [node-developer-guide.md](node-developer-guide.md) の NodeWebUi 節

---

## 1. 概要 — 3 つの拡張レイヤーと「.cs が必要な範囲」

WebUI プラグインは以下の 3 レイヤーの UI を追加できます。

| レイヤー | 描画範囲 | JS だけで完結するか |
|---|---|---|
| **panel** | メニューバー Plugins メニューから開く独立パネル | ✅ **JS のみで完結**（.cs 不要） |
| **widget** | ノード枠内のウィジェット領域（標準ポート行の下） | ❌ 紐付け先ノードの `.cs` が必要 |
| **nodeRenderer** | ノード内側の描画全体（フルカスタムノード UI） | ❌ 紐付け先ノードの `.cs` が必要 |

> **重要: widget / nodeRenderer は「どのノードに UI を付けるか」を C# 側が宣言する仕組みです。**
> ノード型・ポート定義・実行ロジックはサーバー（C#）が持つため、UI プラグイン JS 単体では
> ノードは作れません。ノード UI プラグインは必ず **`.cs`（`[NodeWebUi]` 宣言）とペア**で配布します。
> `.cs` 側は数行のシェルで済みます（§4）。
>
> ノードに紐付かない UI（監視パネル・ツールパネル等）であれば panel レイヤーで JS のみで実現できます。

### 動作の仕組み

```
起動時: WebUI → GET /api/webui-plugins → plugins/ のマニフェスト取得
      → 各 .js を dynamic import → JS が window.NGOL.register*() を呼ぶ
描画時: ノードの customWebUi.pluginId でレジストリを検索
      → nodeRenderer 登録あり → フルノード描画
      → widget 登録あり     → ウィジェット描画
      → 未登録             → 標準表示にフォールバック（エラーにならない）
```

- プラグインの読み込み失敗・描画例外は個別に隔離され、WebUI 本体は必ず起動します
- プラグイン URL には `?v=<ファイル更新時刻>` が付くため、**`.js` を更新したらブラウザ F5 だけで反映**されます（キャッシュ対策不要）

---

## 2. インストール（利用ユーザー向け）

1. 配布された `.js` ファイル（またはフォルダ）を以下に置く:
   ```
   <ngolRoot>/WebUI/plugins/
   ```
2. ノード UI プラグインの場合は、同梱の `.cs` を以下に置く:
   ```
   <ngolRoot>/Nodes/CustomNodes/cs/
   ```
3. WebUI をブラウザで F5 リロード（ホストアプリケーションの再起動は不要。`.cs` はホットリロードされる）

アンインストールはファイル削除 + F5 のみ。壊れたプラグインがあっても WebUI は起動します
（ブラウザ Console に warn が出て該当プラグインだけ無効になる）。

---

## 3. プラグインの作り方（プラグイン作者向け）

### 3.1 配置形式（2 形式）

```
WebUI/plugins/
  my-plugin.js          ← 形式A: 単一ファイル。最短経路
  my-pack/              ← 形式B: メタデータ付きフォルダ
    plugin.json
    index.js
```

**plugin.json（形式 B）**:

```json
{
  "id": "my.webui.pack",
  "version": "1.0.0",
  "displayName": "My Pack",
  "scriptFile": "index.js",
  "apiVersion": 1
}
```

`apiVersion` が WebUI 側の `NGOL.apiVersion` と不一致の場合は警告ログのみで読み込みは継続されます。

### 3.2 基本ルール

- **ES module** として書き、module トップレベルで `window.NGOL` の登録 API を呼ぶ（side-effect 登録）
- **React は自前でバンドルしない**。必ず `window.NGOL.React` を使う
  （別インスタンスの React を混ぜると hooks が壊れます）
- JSX の代わりに `NGOL.html`（[htm](https://github.com/developit/htm) タグ記法）が使えるため**ビルド不要**
- TypeScript + Vite でビルドしたバンドルも同じ仕組みで配置・読み込み可能
  （`react` を external にして `window.NGOL.React` を参照させること）

### 3.3 window.NGOL API リファレンス（apiVersion: 1）

```typescript
window.NGOL = {
  apiVersion: 1,

  React,      // WebUI 本体と同一の React インスタンス
  html,       // htm を React.createElement に bind したタグ関数

  // ---- 登録 API ----
  registerWidget(pluginId, component),                  // ノード内ウィジェット
  registerNodeRenderer(pluginId, component, options?),  // フルノード描画
  registerPanel({ id, title, component, defaultOpen? }),// 独立パネル

  // ---- ノード型 ID 上書き（上級）----
  registerNodeRendererOverride(nodeTypeId, component, options?),  // 任意ノード型をフルノード描画で上書き
  registerWidgetOverride(nodeTypeId, component, options?),        // 任意ノード型にウィジェット追加

  // ---- WebSocket（既存プロトコルをそのまま利用可能）----
  ws: {
    send(data),           // 例: ws.send({ type: 'get_node_list' })
    onMessage(cb),        // 全受信メッセージ購読。戻り値 = 購読解除関数
    onConnection(cb),     // 接続状態変化の購読
  },

  // ---- スナップショット直接書き込み ----
  // UI で決定した値を、対象ノードの断片を実行せずに後続断片へ渡す。
  // value は string / number / boolean / null のみ。PIN 中は拒否される
  // （set_snapshot_value_response の success=false, reason="blocked"）。
  // 注意: ノードの Execute は走らない（副作用・出力ポート実値は変化しない）。
  //       断片リンク経由で値を渡す用途に限定して使うこと。
  setSnapshotValue(nodeInstanceId, portName, value),

  // ---- 永続ノードのライブパラメータ書き込み ----
  // Execute / runFragment なしで params を部分マージする。
  // 永続ノードは C# 側で ctx.GetLiveParam(key, default) を opt-in 読み取り。
  // params の各値は string / number / boolean / null のみ。
  // 応答: push_node_live_params_response { success, nodeInstanceId, mergedKeys, reason? }
  pushLiveParams(nodeInstanceId, params),

  log(...args),           // "[NGOL Plugin]" プレフィックス付きログ

  // ---- nodeRenderer の Handle 座標計算ヘルパー（Phase 3）----
  // portLayout は指定すると既定の自動スプレッド配置を丸ごと置き換えるため、
  // 一部のポートだけ調整したい場合は autoPortLayout() で既定値を取得してから上書きする（§3.5参照）
  layout: {
    autoPortLayout(nodeTypeInfo),  // 既定の自動配置（PORT_BASE起点・PORT_STEP間隔）を計算
    PORT_BASE,                     // データポートの既定開始位置(px) = 68。EXEC_TOPと衝突するため使わないこと
    PORT_STEP,                     // ポート間隔(px) = 26
    EXEC_TOP,                      // exec_in/exec_out 専用の固定座標(px) = 48。portLayoutでは変更不可
  },

  // ---- エディタ拡張ポイント（Phase 2）----
  extensions: {
    registerMenu({ label, items }),            // メニューバー右端にメニュー追加
    registerNodeContextMenuItems(fn),          // ノード右クリックメニュー拡張
    registerOverlay({ id, component }),        // キャンバスオーバーレイ
    useReactFlow,                              // ReactFlow フック（オーバーレイ内でのみ使用可）
  },
  events: {
    on(type, cb),           // イベント購読。戻り値 = 購読解除関数
  },
}
```

#### setSnapshotValue と pushLiveParams の使い分け

| API | 向き | ストア | ノード Execute | 典型用途 |
|---|---|---|---|---|
| `setSnapshotValue(nodeId, portName, value)` | WebUI → 断片/Snapshot | SnapshotStore（ポート名キー） | 走らない | 断片リンクで下流へ値を渡す（リスト選択等） |
| `pushLiveParams(nodeId, params)` | WebUI → 永続ノード | LiveParamStore（任意キー・部分マージ） | 走らない | スライダー等で実行中ノードをリアルタイムチューニング |

`pushLiveParams` は **永続ノードが `GetLiveParam` を呼ぶ実装であること**が前提です。呼ばないノードには効果がありません。
停止時（Stop Persistent）は LiveParamStore から当該ノードのエントリが自動削除されます。

```javascript
// スライダー操作の例（nodeId はグラフ上のノード instanceId）
NGOL.pushLiveParams(nodeId, { scale: 1.2 })
// 応答を待つ場合:
NGOL.ws.onMessage((msg) => {
  if (msg.type === 'push_node_live_params_response' && msg.success)
    console.log('merged:', msg.mergedKeys)
})
```

ノード側の読み取り例は [node-developer-guide.md](node-developer-guide.md) の `GetLiveParam` を参照。

### 3.4 widget — ノード内ウィジェット

```javascript
const { React, html, registerWidget } = window.NGOL

// props: { spec, nodeId, snapshotBadge, snapshotBadgesByPort, paramValues, onParamChange }
//   spec                 : C# [NodeWebUi] が生成した設定 JSON（extra 含む）
//   snapshotBadge        : このノードが最後に SetSnapshot したポートの Snapshot（後方互換、1件のみ）
//   snapshotBadgesByPort : このノードの全ポート分（{ [portName]: { portName, valueType, time, valueString } }）
//   paramValues          : ノードのパラメータ値
//   onParamChange        : (name, value) => void — パラメータ書き込み
function MyWidget({ spec, snapshotBadgesByPort, paramValues, onParamChange }) {
  return html`<div>value: ${snapshotBadgesByPort?.value?.valueString ?? '—'}</div>`
}

registerWidget('my.webui.widget', MyWidget)
```

> **複数ポートに SetSnapshot するノードは必ず `snapshotBadgesByPort` でポート名を指定して読むこと。**
> `snapshotBadge`（単数形）はノードが**最後に** `SetSnapshot` したポートの値しか保持しない。
> 1 回の Execute() で `options` → `selected` のように複数ポートへ順番に SetSnapshot するノード
> （`ngol.snapshot.list_item_selector` 等）で `snapshotBadge` を読むと、後で書いたポートの値が
> 先に書いたポートのバッジを上書きしてしまい「一覧が空に見える」不具合になる。

### 3.5 nodeRenderer — フルノード描画

ノード内側の描画全体を委譲します。**外枠・選択状態・断片実行ボタン・ポート Handle
（接続線のアンカー）は NGOL 側が管理**し、プラグインは内側のコンテンツを自由に描画します。

```javascript
const { html, registerNodeRenderer } = window.NGOL

// props: widget の props に加えて
//   nodeTypeInfo : ノード型情報（ports / displayName / category）
//   selected     : 選択状態
//   runFragment  : このノードを含む断片の実行トリガ
function MyNodeUi({ nodeTypeInfo, paramValues, onParamChange, runFragment }) {
  return html`<div style=${{ padding: '8px' }}>...</div>`
}

registerNodeRenderer('my.webui.nodeui', MyNodeUi, {
  // Handle の縦位置（px）をレイアウトに揃える。省略時: top 68px から 26px 間隔
  portLayout: (nodeTypeInfo) => ({
    inputs: {},                      // ポート名 → top(px)
    outputs: { x: 160, y: 176 },
  }),
})
```

**`portLayout` は指定すると既定の自動配置を丸ごと置き換える**（マージではない）。
ポートを追加したのに更新を忘れると、そのポートは既定開始位置（68px）に集約されて他の
未指定ポートと重なる。一部のポートだけ調整したい場合は `NGOL.layout.autoPortLayout()` で
既定値を取得してから必要な箇所だけ上書きするとよい:

```javascript
registerNodeRenderer('my.webui.nodeui', MyNodeUi, {
  portLayout: (nodeTypeInfo) => ({
    ...NGOL.layout.autoPortLayout(nodeTypeInfo), // 全ポート分の既定配置をまず取得
    outputs: { value: 128 },                     // value だけキャンバスに合わせて上書き
  }),
})
```

注意点:
- ドラッグ操作を受ける要素には `className="nodrag"` を付ける（ノード移動と競合するため）
- ポインタ操作は `onPointerDown` で `e.stopPropagation()` + `setPointerCapture()` を推奨
- ノードサイズはプラグインのルート要素サイズに追従する
- **`top: 48px` は使わないこと**。実行順序専用の合成ハンドル（`__exec_in__`/`__exec_out__`）が
  常にその座標に固定描画されており、`portLayout` では変更できない。データポートをそこに
  置くと重なって見た目上「1点に接続が収束している」ように見える。
  この座標は `NGOL.layout.EXEC_TOP` として参照できる
- 座標を手計算する場合は `NGOL.layout.PORT_BASE`（既定開始位置・68px）と
  `NGOL.layout.PORT_STEP`（間隔・26px）を使うこと。マジックナンバーを直接書く必要はない
- **ポート名・型の一覧を自前の固定配列（`const INPUT_PORTS = ['a', 'b', ...]` 等）に持たないこと。**
  必ず `nodeTypeInfo.ports`（`name`/`direction`/`dataType`）から毎レンダー導出する。固定配列は
  C#側でポートを追加・削除・リネームしても自動更新されず、サーバーは正しいのにWebUIだけ古い
  ポート表示のまま気づかず残り続ける（F5・ハードリロード・シークレットウィンドウいずれも無関係）。
  詳細・実例: `docs/tips/06_webui/nodeui_plugin_port_hardcode.md`

### 3.6 panel — 独立パネル（.cs 不要）

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

- 登録するとメニューバーに **Plugins** メニューが現れ、そこから開閉できる
- パネルはヘッダのドラッグで移動可能（ホスト側が提供）
- `defaultOpen: true` で起動時に自動で開く

### 3.7 エディタ拡張ポイント（extensions / events）

パネルやノード UI に収まらない「エディタ本体への介入」を行う API 群。すべて **JS のみで完結**します（.cs 不要）。

**メニューバー拡張**（メニューバー右端に追加される）:

```javascript
NGOL.extensions.registerMenu({
  label: 'My Tools',
  items: [
    { label: 'Export Report', onClick: () => {...} },
    { separator: true },
    { label: 'Settings', onClick: () => {...} },
  ],
})
```

**ノードコンテキストメニュー拡張**（コールバックがノード情報を受けて項目を返す。対象外ノードは空配列）:

```javascript
NGOL.extensions.registerNodeContextMenuItems((ctx) => {
  // ctx: { nodeId, nodeTypeId, paramValues, isPersistentRunning }
  if (ctx.nodeTypeId !== 'my.custom.node') return []
  return [{ label: 'Inspect Detail', onClick: () => {...} }]
})
```

**キャンバスオーバーレイ**（ReactFlow の子として描画される。ビューポート情報は `NGOL.extensions.useReactFlow()` で取得）:

```javascript
function MyOverlay() {
  const { getZoom, getViewport } = NGOL.extensions.useReactFlow()
  return NGOL.html`<div style=${{ position: 'absolute', top: '8px', right: '8px', pointerEvents: 'none' }}>
    zoom: ${getZoom().toFixed(2)}
  </div>`
}
NGOL.extensions.registerOverlay({ id: 'my.overlay', component: MyOverlay })
```

- オーバーレイのルート要素は自前で `position: absolute` 等の配置を行うこと
- マウス操作を奪わないよう、表示専用なら `pointerEvents: 'none'` を推奨

**イベントフック**:

```javascript
const off = NGOL.events.on('snapshot_saved', (msg) => {...})
// 種別: 'ws_message'（全WS受信） / 'graph_loaded' / 'execution_finished'
//       / 'snapshot_saved' / 'selection_changed'（{ selectedNodeIds }）
off()  // 購読解除
```

- リスナー内の例外は捕捉されログのみ（エディタは止まらない）
- extensions 系は render 後の遅延登録にも UI が追従する（メニュー等は登録した瞬間に現れる）

サンプル: `samples/WebUIPlugins/sample-extensions.js`（4 API すべての使用例）

### 3.8 ノード型 ID による描画上書き（上級）

`[NodeWebUi]` を宣言していない**任意のノード型**（ビルトイン・第三者 `.cs` を問わない）の描画を、
JS 側からノード型 ID を直接指定して上書きできます。**元ノードの作者が関与しない差し替え**であるため、
以下のガードレールが常に適用されます。

```javascript
const { html, registerNodeRendererOverride, registerWidgetOverride } = window.NGOL

// フルノード描画で上書き（props は nodeRenderer と同じ。spec は常に {}）
registerNodeRendererOverride('ngol.logic.add', MyAddUi, {
  label: 'my-ui-pack',                    // 表示・ログ用の登録元ラベル（推奨・省略時は nodeTypeId）
  portLayout: (t) => ({ inputs: { a: 68, b: 94 }, outputs: { result: 68 } }),
})

// 標準描画の下にウィジェットを追加（props は widget と同じ。spec は常に {}）
registerWidgetOverride('ngol.logic.log', MyLogWidget, { label: 'my-ui-pack' })
```

**解決優先順位**: ①型 ID 上書き → ②`[NodeWebUi]` 宣言プラグイン → ③標準描画。
型 ID 上書きが存在するノードでは、宣言プラグインは kind を問わず使用されません。

**ガードレール（本体組み込み・無効化不可）**:

| ガードレール | 動作 |
|---|---|
| Override バッジ | 上書き適用中のノードに紫の `Override` バッジが表示される（title に登録元ラベル） |
| 標準 UI 復帰トグル | メニューバー Plugins > Node Overrides で型 ID 単位にチェック OFF → 標準描画へ即時復帰（セッション内のみ・グラフに保存されない） |
| 失敗時フォールバック | 上書き描画が例外を投げた場合、エラー表示ではなくそのノードインスタンスのみ標準描画へ自動復帰（Console にエラーログ） |
| 衝突検知 | 同一ノード型に複数プラグインが登録した場合は**後勝ち**。console.warn に両ラベルが出力される |

**作者向けの注意**:

- 上書き UI は対象ノードの**ポート名・paramValues の意味という内部仕様に依存**します。対象ノードの
  更新（ポート名変更等）で静かに壊れる可能性があるため、対象ノードのバージョンを README 等に明記してください
- `onParamChange` の書き込みはノードの実行入力に影響します。表示と書き込み値がズレる UI はバグの温床です
- 自作ノードに 独自UI を付ける場合は本機能ではなく `[NodeWebUi]` 宣言（§4）を使ってください。
  型 ID 上書きは「他者のノードに後付けする」ための機構です

---

## 4. ノード側の宣言（ノード開発者向け）

widget / nodeRenderer をノードに紐付けるには、C# ノードクラスに `[NodeWebUi]` を宣言します。

```csharp
[NodeType("my.gauge_probe", "MyNodes", "Gauge Probe")]
[NodePort("value", PortDirection.Input, "number")]
[NodeWebUi("my.webui.gauge",              // プラグイン ID（JS 側の register* と一致させる）
    OptionsFromSnapshot = "value",        // 任意: スナップショット参照ポート名
    BindTo = "selected",                  // 任意: 選択値の書き込み先パラメータ名
    ExtraJson = "{\"max\":\"100\"}")]     // 任意: プラグイン固有設定（spec.extra として渡る）
public sealed class GaugeProbeNode : INode { ... }
```

- `ExtraJson` は**有効な JSON オブジェクト文字列**であること（不正な場合は WebUI が標準表示にフォールバック）
- widget と nodeRenderer のどちらになるかは **JS 側がどちらを登録したかで決まる**
  （同じ pluginId に両方登録されている場合は nodeRenderer 優先）
- 対応プラグインが未インストールの環境では標準テキスト表示で動作継続する（graceful degradation）

詳細は [node-developer-guide.md](node-developer-guide.md) を参照。

---

## 5. サンプル

`samples/WebUIPlugins/` に 3 レイヤーそれぞれのノービルドサンプルがあります。

| サンプル | レイヤー | ペアの .cs |
|---|---|---|
| `sample-gauge.js` | widget（ゲージバー表示、`extra.max` 使用） | `samples/CustomNodes/custom_webui_samples/GaugeProbeNode.cs` |
| `sample-fullnode.js` | nodeRenderer（XY パッド、`portLayout` 使用） | 同 `XyPadNode.cs` |
| `sample-live-waveform.js` | nodeRenderer（Canvas ライブ波形、`PushLiveValue` + `snapshotBadgesByPort` 使用） | `LiveWaveformNode.cs`（`frequency`/`amplitude` 入力で `WaveformControlNode` と連携可） |
| `sample-waveform-control.js` | widget（周波数・振幅スライダー、`pushLiveParams` 使用） | `WaveformControlNode.cs`（出力を `LiveWaveformNode` に接続するとライブ連携デモになる） |
| `list-item-selector.js` | nodeRenderer（リスト要素ピッカー・ピル/リスト手動切替・`setSnapshotValue`） | `ListItemSelectorNode.cs` |
| `sample-panel/` | panel（snapshot_saved 監視、形式 B） | 不要 |
---

## 6. トラブルシューティング

| 症状 | 原因と対処 |
|---|---|
| プラグイン UI が出ない・標準表示のまま | pluginId の不一致（C# `[NodeWebUi]` と JS `register*` を照合）。ブラウザ Console で `[NGOL Plugin] Loaded:` を確認 |
| Console に `Failed to load '...'` | JS の構文エラー等。該当プラグインのみ無効・他は動作継続。修正して F5 |
| ノード内に赤枠で `Plugin error: <id>` | 描画中の例外（ErrorBoundary が隔離）。Console のスタックトレースを確認 |
| hooks のエラー（Invalid hook call 等） | React を自前 import している。`window.NGOL.React` を使うこと |
| `.js` を更新したのに反映されない | F5 リロードしたか確認（`?v=` により通常は F5 で確実に反映される） |
| パッド等のドラッグ UI でノードごと動く | 対象要素に `className="nodrag"` を付け、`onPointerDown` で `stopPropagation()` |
| `npm run dev`（Vite）でプラグインが読み込まれない | 仕様。`/api/webui-plugins` はゲームサーバーのみ提供。実配置で確認する |

---

## 7. バージョンポリシー

- `NGOL.apiVersion`（現在 **1**）: `window.NGOL` の破壊的変更時にインクリメント
  （`extensions` / `events` は加算的追加のため apiVersion 1 のまま）
- plugin.json の `apiVersion` と不一致でも読み込みは継続（警告のみ）
