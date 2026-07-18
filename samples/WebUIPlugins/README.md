# WebUI Plugins — Samples

WebUI を再ビルドせずに、`.js` ファイルを配置するだけで WebUI に UI を追加できる
外部 UI プラグインのサンプル集。

> **⚠️ 「JS だけで完結するか」はレイヤーによって異なります。**
>
> | レイヤー | 内容 | .cs（カスタムノード）の要否 |
> |---|---|---|
> | panel | 独立パネル（Plugins メニューから開く） | **不要** — JS のみで完結 |
> | widget | ノード内ウィジェット | **必要** — 紐付け先ノードの `.cs` に `[NodeWebUi]` 宣言 |
> | nodeRenderer | フルノード描画 | **必要** — 同上 |
>
> ノード型・ポート定義・実行ロジックはサーバー（C#）側にあるため、
> **ノード UI プラグイン（widget / nodeRenderer）は必ず `.cs` とペアで配布**します。
> JS が担当するのは描画のみです。詳細は `docs/webui-plugin-guide.md` §1 参照。

## デプロイ先

```
<ngolRoot>/WebUI/plugins/
```

配置後、ブラウザを F5 リロードすると読み込まれる（キャッシュは自動バイパス）。

## サンプル一覧

| サンプル | 形式 | 拡張種別 | 対応カスタムノード |
|---|---|---|---|
| `sample-gauge.js` | A: 単一 .js | widget（ノード内ウィジェット） | `../CustomNodes/custom_webui_samples/GaugeProbeNode.cs` |
| `sample-fullnode.js` | A: 単一 .js | nodeRenderer（フルノード描画） | `../CustomNodes/custom_webui_samples/XyPadNode.cs` |
| `sample-live-waveform.js` | A: 単一 .js | nodeRenderer（Canvas ライブ波形、`PushLiveValue` 使用） | `../CustomNodes/custom_webui_samples/LiveWaveformNode.cs` |
| `sample-waveform-control.js` | A: 単一 .js | widget（周波数・振幅スライダー、`pushLiveParams` 使用） | `../CustomNodes/custom_webui_samples/WaveformControlNode.cs`（出力を `LiveWaveformNode` の `frequency`/`amplitude` 入力へ接続するとライブ連携） |
| `list-item-selector.js` | A: 単一 .js | nodeRenderer（ピル / リスト切替・`setSnapshotValue`） | `../CustomNodes/custom_webui_samples/ListItemSelectorNode.cs` |
| `text-box-nodeui.js` | A: 単一 .js | nodeRenderer（ノード型ID上書き・`registerNodeRendererOverride`） | ビルトイン `ngol.logic.text_box`（`[NodeWebUi]` 宣言なし。本プラグイン未配置でも標準描画で動作） |
| `sample-panel/` | B: フォルダ + plugin.json | panel（Plugins メニューのパネル） | 不要（単体で動作） |
## 2 つの配置形式

- **形式A**: `plugins/*.js` — 1 ファイル置くだけ。ノービルドの最短経路
- **形式B**: `plugins/<dir>/plugin.json` + `index.js` — バージョン等のメタデータ付き

## プラグインの書き方（要点）

- ES module として書き、module トップレベルで `window.NGOL` の登録 API を呼ぶ（side-effect 登録）
- React は自前でバンドルせず、必ず `window.NGOL.React` を使う（別インスタンスだと hooks が壊れる）
- JSX の代わりに `window.NGOL.html`（htm タグ記法）が使える
- ドラッグ操作を受ける要素には `className="nodrag"` を付けてノード移動と競合しないようにする
- 詳細は `docs/webui-plugin-guide.md`（完全ガイド）と `WebUI/src/webuiPlugin/ngolGlobalApi.ts` を参照

```javascript
const { React, html, registerWidget } = window.NGOL

function MyWidget({ spec, snapshotBadge, paramValues, onParamChange }) {
  return html`<div>value: ${snapshotBadge?.valueString ?? '—'}</div>`
}

registerWidget('my.webui.widget', MyWidget)
```

C# ノード側はプラグイン ID を宣言するだけ:

```csharp
[NodeWebUi("my.webui.widget", OptionsFromSnapshot = "value", ExtraJson = "{\"max\":\"100\"}")]
public sealed class MyNode : INode { ... }
```
