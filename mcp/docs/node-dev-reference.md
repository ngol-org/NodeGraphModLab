# NGOL カスタムノード開発リファレンス（AI 向け）

`compile_node` / `save_node_source` で C# ノードを書く前に読むこと。

## 最小テンプレート（コピペ可能）

```csharp
using System;
using NodeGraphModLab.NodeAPI;

[NodeType(
    "custom.math.multiply",      // ノード ID（グローバル一意。命名規則は下記）
    "Custom/Math",                // カテゴリ（スラッシュ区切り階層）
    "Multiply",                   // WebUI 表示名
    Description = "2つの数値を乗算します")]
[NodePort("a",      PortDirection.Input,  "number", IsRequired = true)]
[NodePort("b",      PortDirection.Input,  "number", IsRequired = true)]
[NodePort("result", PortDirection.Output, "number")]
public sealed class MultiplyNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var a = Convert.ToDouble(ctx.GetPortValue("a") ?? ctx.GetParam<double>("a"));
        var b = Convert.ToDouble(ctx.GetPortValue("b") ?? ctx.GetParam<double>("b"));
        ctx.SetPortValue("result", a * b);
    }
}
```

**値解決の順序**: ポート接続 (`GetPortValue`) → paramValues (`GetParam<T>`) → デフォルト値。

## 属性ルール

| 属性 | 必須引数 | 命名規則 |
|---|---|---|
| `[NodeType]` | `nodeTypeId, category, displayName` | ID は `{prefix}.{category}.{name}` 小文字スネークケース。`ngol.*` は組み込み専用。独自プレフィックスを使うこと（例: `custom.*`, `myapp.*`）。省略可の`Version`（SemVer文字列、既定`"1.0.0"`）で`nodeVersion`を明示できる（`get_node_detail`/WebUI Inspectorに表示。グラフ保存時の版と食い違うとWebUIが警告するのみで実行はブロックしない） |
| `[NodePort]` | `name, direction, dataType` | `IsRequired`（Input のみ）・`ShowInlineEditor`（Output のみ。`false` で未接続時のインライン入力欄を非表示）・`Description` は任意。同一クラスに複数付与可、Input は宣言順に WebUI 表示 |

## データ型一覧（`dataType` 文字列）

型チェックは実行時に一切行われない（WebUI 表示用メタデータ）。`is`/`as`/`Convert` で自前検証すること。

| 文字列 | C# 実体 |
|---|---|
| `"number"` / `"float"` / `"int"` | `double`（Boxing あり） |
| `"string"` | `string` |
| `"bool"` / `"boolean"` | `bool` |
| `"any"` | `object?` |
| `"list"` | `IEnumerable<object?>`（`List<object?>` として materialize 可能） |
| ホスト定義の任意オブジェクト型（例: `"gameobject"` / `"transform"` / `"component"` / `"material"` 等） | `object`（ホスト側の型をリフレクション経由で扱う。Unity ゲームエンジンをホストにした場合の一例） |
| `"vector2"` / `"vector3"` / `"vector4"` / `"color"` | `object`（struct Boxing） |

## IExecutionContext API 早見表

| メンバー | 用途 |
|---|---|
| `NodeInstanceId` | このノードインスタンスの ID |
| `GetPortValue(string)` / `SetPortValue(string, object?)` | ポート値の読み書き |
| `GetParam<T>(string)` | Inspector 固定値の取得 |
| `Logger.LogInfo/LogWarning/LogError/LogDebug` | ログ出力（ホストプロセスのログ + WebUI 両方に表示） |
| `MainThreadDispatch(Action)` | ホストのメインスレッドへ処理をキュー（下記スレッド制約ルール必読） |
| `RegisterPersistent(PersistentCallbacks)` | 毎フレーム/毎tickコールバック登録（下記表） |
| `SnapshotStore` | 断片間の値受け渡し（`SetSnapshot`/`GetSnapshot`） |
| `GetDownstreamConnections(string)` | 出力ポートの下流接続一覧 |
| `PushLiveValue(string, object?)` | Snapshot をリアルタイム更新（`SetPortValue` と違い断片実行を待たず即時反映） |
| `GetLiveParam<T>(string key, T defaultValue = default)` | WebUI の `pushLiveParams` が書き込んだライブパラメータを読む（永続ノードの `OnUpdate` で毎フレーム呼ぶ。opt-in） |
| `Store` | ノード間・ホストプロセス再起動をまたぐ KV Store |

## メインスレッド制約ルール（必読・ホストによっては違反するとクラッシュする）

ホストのランタイムによっては、特定 API を専用スレッド（メインスレッド）からしか呼べない制約がある（例: Unity ゲームエンジンをホストにした場合の Unity API 呼び出し）。このようなホストと連携する場合は以下を守る。

- `Execute()` は**バックグラウンドスレッド**で呼ばれる。制約付き API を直接呼ばない
- 制約付き API は必ず `ctx.MainThreadDispatch(Action)` でラップする
- ホストによっては `MainThreadDispatch` 内で `try-catch` が機能しない場合がある（例: IL2CPP AOT コンパイル環境）。null チェックで防御するのが安全
- 完了待ちは `ManualResetEventSlim` + `done.Wait(TimeSpan.FromSeconds(N))`（2〜10 秒目安）

```csharp
var done = new System.Threading.ManualResetEventSlim(false);
ctx.MainThreadDispatch(() =>
{
    // ここでホストのメインスレッド専用 API を呼ぶ（try-catch が機能しないホストもあるため注意）
    done.Set();
});
done.Wait(System.TimeSpan.FromSeconds(5));
```

## RegisterPersistent（毎フレーム/毎tick処理・ホットリロード安全）

`Execute()` 内で登録すると、実行後もコールバックが継続する。WebUI に **PERSISTENT** バッジが表示される。

| コールバック（`PersistentCallbacks` のプロパティ） | 発火タイミング | 用途 |
|---|---|---|
| `OnStart` | 登録直後に 1 回 | 初期化 |
| `OnUpdate` | 毎フレーム/毎tick（メインスレッド） | メインスレッド専用 API を直接呼べる。`MainThreadDispatch` 不要 |
| `OnStop` | 停止時（`IsActive=false` になった時。Cancel 経路でも発火） | クリーンアップ |

基本の `PersistentCallbacks` が持つのはこの3つ（`OnUpdate`/`OnStart`/`OnStop`、いずれも PascalCase の C# プロパティ）のみ。
ホスト固有の追加フェーズ（例えば Unity ホストなら `OnGUI`/`FixedUpdate`/`LateUpdate` 相当のもの）は、
`PersistentCallbacks` 自体には存在せず、ホストブリッジ側がこれを継承して独自プロパティを追加した
サブクラス経由でのみ使える（`GetPhase(string)` をオーバーライドしてフェーズ名を解決する仕組み）。
基本 3 つのプロパティ以外が必要な場合は、そのホストが提供するサブクラスの型名・プロパティ名を
`get_available_nodes` 等で確認すること。

- `OnUpdate`（またはホスト拡張コールバック）内も、ホストによっては `try-catch` が機能しない場合がある。null チェックで防御する
- `Thread.Sleep` / `Task.Run` は呼ばない（メインスレッドがフリーズする）
- 停止: WebUI 右クリック → Stop Persistent、または MCP `stop_persistent_node`

> **注意（ホスト固有の制約例）**: Unity IL2CPP のようなランタイムでは、インラインで `MonoBehaviour` 相当のコンポーネントを定義して動的登録するパターンが、ホットリロード後に型初期化エラーでクラッシュしうる。毎フレーム処理・GUI 表示は基本的に `RegisterPersistent` を使うこと。

### Job自動追跡・fail-fast・進捗報告（`check_job_status`）

`RegisterPersistent` を呼ぶと自動でJobが1つ発行される（ノード側の変更不要）。`execute_graph` 等のレスポンスの
`jobs: [{jobId, nodeInstanceId}]` からjobIdを取得し、MCP `check_job_status` でポーリングして完了・失敗を確認できる。
グラフの実行自体が長時間かかる場合は `execute_graph` 等に `async: true` を渡すことで、結果を待たずjobIdを即座に受け取れる。

- **fail-fast**: `OnStart`/`OnUpdate`（またはホスト拡張コールバック）から例外が漏れると、Jobが`Failed`になり登録は自動停止する（次の更新サイクルから呼ばれない）。一過性のエラーで動き続けたい場合は該当コールバック内で自前に `try/catch` すること（re-throwしなければフレームワークに届かず継続する）。
- **`OnStop`**: 例外時はJobを`Failed`にするが（クリーンアップ失敗として記録）、既に停止処理中のため追加の自動停止はしない。
- **`ReportProgress(string message)`**（`IPersistentRegistration`、オプトイン）: 呼べば`check_job_status`の`message`に自由記述の状況（進捗・フェーズ名等）がそのまま反映される。呼ばなければ`null`のまま。

```csharp
IPersistentRegistration? reg = null;
reg = ctx.RegisterPersistent(new PersistentCallbacks
{
    OnUpdate = () =>
    {
        // ...長時間処理の1ステップ...
        reg?.ReportProgress($"{done}/{total} 完了");
    },
});
```

## ctx.Store（KV Store・ホストプロセス再起動後も永続）

```csharp
ctx.Store.Set("myapp.lastTarget", "Player");   // string/long/double/bool/byte[] 対応
var v = ctx.Store.Get<string>("myapp.lastTarget");
ctx.Store.Delete("myapp.lastTarget");
```

キー名は `{prefix}.{key}` 形式を推奨（他ノードとの衝突回避）。

## compile_node / save_node_source の folder パラメータ

| folder 値 | 保存先 | 用途 |
|---|---|---|
| 省略（デフォルト `"ai_generated"`） | `cs/ai_generated/ClassName.cs` | 永続的なノード |
| 任意の名前（例 `"analysis"`, `"my_project"`） | `cs/<folder>/ClassName.cs` | 用途別に整理したい場合 |

保存後はホットリロードが自動で走る。コンパイル失敗時はレスポンスにエラー・診断が含まれるので、
修正して再度 `compile_node`/`save_node_source` を呼ぶ。

## 複数ファイル構成のノード（`.srclist` / `.rsp`）

1つのノードを複数 `.cs` ファイルに分割したい場合、`save_node_source` で同じ `folder` に以下も保存する
（`.srclist`/`.rsp` は `fileName` が `.cs` 必須の制約があるため `save_node_source` では書けない。
`Write` 等で直接同じフォルダに置く）:

- **`Foo.srclist`**（`Foo.cs` と同名・任意）: 追加で同梱する `.cs` の相対パスを1行1つ列挙。`#`コメント可、
  末尾 `/` はディレクトリ一括指定（直下のみ・非再帰）、末尾 `/**` はサブディレクトリ含め再帰的に一括指定。
  `[NodeType]` を持たない共有ファイルを更新すると、参照している
  ノードが自動で再コンパイルされる。無ければ従来通り単一ファイルコンパイル
- **`Foo.rsp`**（`Foo.cs` と同名・任意）: csc.exe 互換のコンパイラオプション（`/r:` `/define:` `/nowarn:` `/analyzer:` 等）
  のみを書く。ソースファイルの列挙はしない（それは `.srclist` の役割）。`/r:` は `.rsp` からの相対パス
  （`/r:lib/Foo.dll` や `/r:../shared/Foo.dll` も可）。コンパイル成功≠実行時に参照 DLL がロードできる
  とは限らない点に注意。`/analyzer:` は事前ビルド済み `IIncrementalGenerator` DLL を実行して生成コードを
  合流させる（クローンした OSS がコンパイル時コード生成に依存する場合に使用。レガシー
  `ISourceGenerator` は非対応）

## 断片グラフ連携（Snapshot / PushLive）の要点

- 断片（Fragment）はグラフ内の連結成分。`fragmentLinks` で断片間を Snapshot 経由接続する
- 下流断片は `SnapshotStore` に保存済みの値を読む。**ノードを実行しないと値は更新されない**
- リアルタイムに値を流したい場合（UI からの逐次更新など）は `PushLiveValue` を使う

## 永続ノードのライブパラメータ（LiveParamStore）

WebUI プラグインが `NGOL.pushLiveParams(nodeInstanceId, params)` を送ると、GraphServer の `LiveParamStore` に部分マージされる。
永続ノードは **`GetLiveParam` を呼んだときだけ** 値が見える（既存ノードへの影響なし）。

```csharp
ctx.RegisterPersistent(new PersistentCallbacks
{
    OnUpdate = () =>
    {
        var scale = ctx.GetLiveParam("scale", 1.0);
        // scale を毎フレーム適用…
    },
});
```

| チャンネル | 方向 | WebUI API | ノード API | 用途 |
|---|---|---|---|---|
| Snapshot | 双方向（ポート名） | `setSnapshotValue` | `PushLiveValue` / `GetSnapshot` | 断片間の値受け渡し |
| LiveParam | WebUI→ノード（現状） | `pushLiveParams` | `GetLiveParam` | 永続ノードのスライダーチューニング |
| LiveParam 逆 | ノード→WebUI | （将来予定） | `PushLiveParam`（未実装） | ノード状態の UI 反映 |

- `params` / 読み取り値は **string / number / boolean / null** のみ
- 履歴なし（最新値のみ）。停止時に `ClearNode`
- `set_snapshot_value` では永続ノードの `OnUpdate` 中の値は更新されない（Snapshot は Execute 時にキャプチャされるため）

## バージョン互換性

カスタムノードが依存するのは `NodeGraphModLab.NodeAPI.dll` の **NodeAPI Version** のみ（ホストプロセス本体/WebUIのProduct Versionとは独立）。
NodeAPI が MINOR 更新された場合のみ `CHANGELOG.md` の `### Breaking` を確認し、必要なら再コンパイルする。PATCH更新はソース互換に影響しない。
