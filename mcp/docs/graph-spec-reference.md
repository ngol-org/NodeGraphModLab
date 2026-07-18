# NGOL グラフ JSON 仕様リファレンス（AI 向け）

`save_graph` / `execute_graph` / `execute_all_fragments` / `execute_fragment` でグラフ JSON を
構築する前に読むこと。

## トップレベル構造

```json
{
  "id":            "string (UUID)",
  "name":          "string",
  "description":   "string",
  "schemaVersion": "0.2.0",
  "nodes":         [ ...NodeInstance ],
  "connections":   [ ...NodeConnection ],
  "fragmentLinks": [ ...FragmentLink ],
  "groups":        [ ...NodeGroup ],
  "annotations":   [ ...NodeAnnotation ]
}
```

| フィールド | 必須 | 説明 |
|---|---|---|
| `id` / `name` | ✅ | グラフの一意 ID・表示名 |
| `schemaVersion` | ✅ | 現行 `"0.2.0"` |
| `nodes` | ✅ | ノードインスタンス配列 |
| `connections` | ✅ | 通常ポート接続（同一断片内） |
| `fragmentLinks` | ✅（複数断片時） | 断片間リンク（Snapshot 経由） |
| `groups` / `annotations` | 省略可 | ノードグループ・付箋。省略時は空配列扱い |

## NodeInstance

```json
{
  "instanceId":  "string (UUID)",
  "nodeTypeId":  "ngol.logic.add",
  "position":    { "x": 100.0, "y": 200.0 },
  "paramValues": { "a": 10, "b": 20 },
  "size": { "width": 220.0, "height": 140.0 }
}
```

`size` は省略可（ユーザーが角ドラッグで手動リサイズした場合のみ設定される）。省略時は自動サイズ。

`position` は必ず設定すること（省略すると全ノードが座標 0 で重なる）。ノード間は 250〜300px 離すこと。

⚠️ **実際の描画幅は不明（`size` 省略時は内容に応じた自動サイズ）だからといって、300px より広く間隔を空けないこと。** 250〜300px は「重ならない」ためではなく「見やすい」ための目安であり、既にノード本体の想定幅を含んだ値。不安だからと400px以上空けると、大半のノード（幅200px前後）ではエッジだけが間延びした見づらいグラフになる。コード表示パネルのように内容量で幅が伸びるノードが混ざる場合のみ、上限寄り（300px）を使う。

## NodeConnection

同一断片内のポート接続。

```json
{ "fromNodeInstanceId": "uuid-A", "fromPortName": "result",
  "toNodeInstanceId": "uuid-B",   "toPortName": "value" }
```

### 予約ポート名（実行順序専用・データなし）

**全ノードが常時持つ**合成ポート。データは運ばず、トポロジカルソート上の実行順序のみを表す。

| 予約名 | 用途 |
|---|---|
| `__exec_in__` | 入力側の実行順序ハンドル |
| `__exec_out__` | 出力側の実行順序ハンドル |

ポート名の実在性はエンジン側で検証されないため、通常のポート名と同様に接続に使える。

⚠️ **Snapshot ノード（`ngol.snapshot.*` および `ctx.SnapshotStore?.SetSnapshot(...)` を呼ぶカスタムノード。List Item Selector 等の WebUI 対話選択ノードを含む）の出力を下流ノードへ渡す場合は、`connections` ではなく次項の `FragmentLink` を使うこと。** `connections` で繋ぐと下流ノードが同一断片（連結成分）に取り込まれ、Snapshot ノードを含む上流全体が毎回まとめて再実行されてしまう。特に WebUI のドロップダウン等で選択値を都度変える対話的ノード（`setSnapshotValue` で SnapshotStore を直接更新するパターン）では、下流を独立した断片にして `execute_fragment` で下流だけ再実行できるようにするのが正しい設計。

## FragmentLink

断片間の Snapshot 経由接続。`sourceSnapshotNodeInstanceId` は必ず Snapshot ノード。

```json
{ "sourceSnapshotNodeInstanceId": "uuid-snapshot", "sourcePortName": "gameobject",
  "toNodeInstanceId": "uuid-target", "toPortName": "gameobject" }
```

通常の `connections` には含まれず、断片の連結成分計算からも除外される。Snapshot ノードの出力を下流に渡す接続は原則こちらを使う（上記 NodeConnection 項参照）。

## データ型一覧（`dataType` 文字列）

| 型名 | C# 実体 |
|---|---|
| `number` / `float` / `int` | `double` |
| `string` | `string` |
| `boolean` / `bool` | `bool` |
| `any` | `object?` |
| `any[]` / `list` | `List<object?>` |
| `GameObject` / `gameobject` | Unity GameObject（IL2CPP リフレクション経由） |
| `Color` | `{r, g, b, a}` オブジェクト |
| `Vector3` | `{x, y, z}` オブジェクト |

## paramValues の解決順序

1. **ポート接続**（前ノードの出力） 2. **paramValues**（JSON の固定値） 3. **デフォルト値**（ノード実装のフォールバック）

ノード実装側の `ctx.GetPortValue(name)` は 1→2 を自動フォールバックする。未接続ポートに `paramValues` を設定すれば、配線しなくても値が渡る。

Color 型 paramValues の形式: `{ "color": { "r": 1.0, "g": 0.5, "b": 0.0, "a": 1.0 } }`

## 実行モデル

- **断片（Fragment）** = `connections` のみで繋がった連結成分（Union-Find で自動計算）。
  断片 ID は `auto-{連結成分内の最小 nodeInstanceId}` で安定生成される
- `execute_graph` — 断片数 ≤ 1 の単純グラフを丸ごと実行
- `execute_fragment` — 指定断片 1 つを実行（上流の依存断片は自動実行される）
- `execute_all_fragments` — 全断片を依存順に実行。`pinnedNodeIds` で特定 Snapshot をスキップ可能
- Snapshot ノードが `SnapshotStore` に値を保存し、`fragmentLinks` 経由で下流断片の入力に注入される
- ノードを実行しない限りスナップショット値は更新されない（UI 操作だけでは反映されない）

## 完全なサンプル

```json
{
  "id": "sample-add-log",
  "name": "Add and Log",
  "schemaVersion": "0.2.0",
  "nodes": [
    { "instanceId": "node-const-a", "nodeTypeId": "ngol.logic.const_number",
      "position": { "x": 0, "y": 0 },   "paramValues": { "value": 10 } },
    { "instanceId": "node-const-b", "nodeTypeId": "ngol.logic.const_number",
      "position": { "x": 0, "y": 80 },  "paramValues": { "value": 20 } },
    { "instanceId": "node-add", "nodeTypeId": "ngol.logic.add",
      "position": { "x": 300, "y": 40 }, "paramValues": {} },
    { "instanceId": "node-log", "nodeTypeId": "ngol.logic.log",
      "position": { "x": 600, "y": 40 }, "paramValues": { "label": "Total" } }
  ],
  "connections": [
    { "fromNodeInstanceId": "node-const-a", "fromPortName": "value",  "toNodeInstanceId": "node-add", "toPortName": "a" },
    { "fromNodeInstanceId": "node-const-b", "fromPortName": "value",  "toNodeInstanceId": "node-add", "toPortName": "b" },
    { "fromNodeInstanceId": "node-add",     "fromPortName": "result", "toNodeInstanceId": "node-log", "toPortName": "value" }
  ],
  "fragmentLinks": []
}
```
