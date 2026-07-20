# カスタムノード開発ガイド — Node Graph Mod Lab (NGOL)

**対象読者**: NGOL のカスタムノードを C# で作成したい方  
**前提知識**: C# の基礎

> 本ガイドは入門者向けの要約版です。**完全版はリポジトリの `docs/node-developer-guide.md`**
> を参照してください（属性の全オプション・実行環境ごとの詳細・KV Store・ForEach 等）。
> AI エージェントは MCP ツール `get_node_dev_guide` で AI 向けリファレンスを取得できます。

---

## 目次

1. [ホットリロードで始める](#1-ホットリロードで始める)
2. [最小構成のノード](#2-最小構成のノード)
3. [属性リファレンス](#3-属性リファレンス)
4. [ポートのデータ型](#4-ポートのデータ型)
5. [IExecutionContext — 主要 API](#5-iexecutioncontext--主要-api)
6. [ホスト固有 API を呼び出す](#6-ホスト固有-api-を呼び出す)
7. [永続ノード（毎フレーム処理・GUI 表示）](#7-永続ノード毎フレーム処理gui-表示)
8. [ノード間共有 KV Store](#8-ノード間共有-kv-store)
9. [DLL として配布する](#9-dll-として配布する)
10. [トラブルシューティング](#10-トラブルシューティング)

---

## 1. ホットリロードで始める

**ホストアプリケーションを再起動せずに**ノードを追加・修正できます。

1. `ngolRoot/Nodes/CustomNodes/cs/` に `.cs` ファイルを置く
2. ファイルを保存する
3. 起動ログに以下が表示されれば成功:

```
[RoslynCompiler] Registered dynamic node: mymod.math.multiply
[Scripts] Hot-reloaded: mymod.math.multiply (MultiplyNode.cs)
```

4. WebUI のノードパレットに新しいノードが表示される

> サブディレクトリも監視対象です（例: `cs/MyMod/`）。

### コンパイルエラー時

```
[Scripts] Hot-reload failed: MultiplyNode.cs — (1,10): error CS1002: ; expected
```

エラーを修正して保存すれば、次のホットリロードで自動的に再試行されます。**コンパイルが成功するまで古いコードが動き続けます**。

---

## 2. 最小構成のノード

```csharp
using System;
using NodeGraphModLab.NodeAPI;

[NodeType(
    "mymod.math.multiply",   // ノード ID（グローバル一意）
    "MyMod/Math",            // カテゴリ（スラッシュ区切り）
    "Multiply",              // WebUI 表示名
    Description = "2つの数値を乗算します")]
[NodePort("a",      PortDirection.Input,  "number", Description = "第1オペランド")]
[NodePort("b",      PortDirection.Input,  "number", Description = "第2オペランド")]
[NodePort("result", PortDirection.Output, "number", Description = "a × b")]
public sealed class MultiplyNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var a = Convert.ToDouble(ctx.GetPortValue("a") ?? ctx.GetParam<double>("a"));
        var b = Convert.ToDouble(ctx.GetPortValue("b") ?? ctx.GetParam<double>("b"));
        ctx.SetPortValue("result", a * b);
        ctx.Logger.LogInfo($"[Multiply] {a} × {b} = {a * b}");
    }
}
```

**実行の流れ**:
- `GetPortValue("a")` → 上流ノードから接続された値（接続なしは `null`）
- `GetParam<double>("a")` → WebUI の Inspector で設定した固定値。未設定時は `double` の既定値 `0.0` を返す
- `SetPortValue("result", value)` → 下流ノードへ値を渡す

> **`double`/`float`/`int` 等の値型に対して `GetParam<T>(...) ?? リテラル` は書かない**: `GetParam<T>` は非制約ジェネリック `T?` で宣言されているため、`T` が非nullable値型の場合は `T?` が `Nullable<T>` ではなく素の `T` に解決され、`??` の左辺が非nullable値型になってコンパイルエラー（CS0019）になる。未設定時は元々 `default(T)` を返すため、フォールバック値が `default(T)`（`double`/`float`/`int` なら `0`）で十分な場合は `?? リテラル` 自体が不要。`string` 等の参照型では `T?` が正しく nullable になるため問題ない（例: `ctx.GetParam<string>("name") ?? "Cube"`）。

---

## 3. 属性リファレンス

### [NodeType]

```csharp
[NodeType(
    string nodeTypeId,    // 必須: グローバル一意 ID（小文字スネークケース）
    string category,      // 必須: カテゴリ（"A/B/C" 形式で階層化）
    string displayName,   // 必須: WebUI に表示される名前
    Description = "...",  // 省略可: ツールチップ説明文
    Version = "1.0.0"     // 省略可: ノード型のバージョン（SemVer想定）。省略時は "1.0.0"
)]
```

**`Version`**: WebUI Inspectorや`get_node_detail`に`nodeVersion`として表示される。保存済みグラフのノードが持つ版と食い違うとWebUIが警告を出す（実行はブロックされない）。詳細は`docs/node-developer-guide.md`参照。

**ノード ID の命名規則**: `{プレフィックス}.{カテゴリ}.{ノード名}`

```
mymod.scene.find_object    ← 自作ノード（独自プレフィックスを使う）
ngol.logic.add             ← 組み込みノード（ngol.* は使わない）
```

### [NodePort]

```csharp
[NodePort(
    string name,              // 必須: ポート名
    PortDirection direction,  // 必須: Input / Output
    string dataType,          // 必須: 型文字列（後述）
    IsRequired = false,       // Input のみ: true で接続または固定値が必須
    Description = "..."       // 省略可: ツールチップ
)]
```

同一クラスに複数付与できます。Input ポートは宣言順に WebUI へ表示されます。

---

## 4. ポートのデータ型

`dataType` 文字列は WebUI の色分け・接続ガイド用のメタデータです。C# の型チェックはフレームワークが行わないため、ノード実装者が `is`/`as`/`Convert` で処理します。

| dataType | 実際の C# 型 |
|---|---|
| `"number"` | `double` / `float` / `int`（Boxing あり） |
| `"string"` | `string` |
| `"bool"` / `"boolean"` | `bool`（Boxing あり。どちらの文字列も同じ扱い） |
| `"any"` | `object?`（任意の型を受け入れる） |
| `"list"` | `List<object?>` |
| `"gameobject"` / `"component"` / `"vector3"` 等 | ホスト固有の型（例: ゲームエンジンの GameObject・Component・Vector3）を `object` として扱う場合の慣例的な名前。dataType 文字列は自由に定義できる |

---

## 5. IExecutionContext — 主要 API

```csharp
public void Execute(IExecutionContext ctx)
{
    // ポート値の読み書き
    object? val = ctx.GetPortValue("portName");
    ctx.SetPortValue("portName", someValue);

    // Inspector の固定値
    string? label = ctx.GetParam<string>("label");
    double  speed  = ctx.GetParam<double>("speed"); // 未設定時は 0.0（値型は ?? リテラル を書かない）

    // ログ出力（ホストのログ + WebUI 両方に表示）
    ctx.Logger.LogInfo("[MyNode] 正常");
    ctx.Logger.LogWarning("[MyNode] 注意");
    ctx.Logger.LogError("[MyNode] エラー");

    // ホストのメインスレッドへ処理をキュー（後述）
    ctx.MainThreadDispatch(() => { /* ホスト固有 API */ });

    // Snapshot（断片間値受け渡し）
    ctx.SnapshotStore?.Set("myKey", someValue);
    var saved = ctx.SnapshotStore?.Get("myKey");
}
```

---

## 6. ホスト固有 API を呼び出す

`Execute()` はバックグラウンドスレッドで呼ばれます。ホスト側の API がメインスレッドからのみ
呼べる場合（多くのゲームエンジン等）は、**`MainThreadDispatch`** でラップします。

以下はホストが Unity ベースの場合の例です（`UnityEngine.*` はホスト側が提供する型であり、
NGOL Core 自体は依存しません。リフレクション経由で呼び出します）。

```csharp
public void Execute(IExecutionContext ctx)
{
    var objName = ctx.GetParam<string>("name") ?? "Cube";

    ctx.MainThreadDispatch(() =>
    {
        // ここがホストのメインスレッドで実行される
        var type = Type.GetType("UnityEngine.GameObject, UnityEngine.CoreModule");
        var findMethod = type?.GetMethod("Find", new[] { typeof(string) });
        var go = findMethod?.Invoke(null, new object[] { objName });
        ctx.SetPortValue("result", go);
    });
}
```

> **`MainThreadDispatch` 内での注意点**:
> - `try-catch` は使わない（一部の実行環境では正しく機能しない）
> - 2 秒以内に終わる処理のみ（タイムアウトあり）
> - 戻り値は取れない（fire-and-forget）

**毎フレーム呼びたい場合**は次の「永続ノード」を使ってください。

---

## 7. 永続ノード（毎フレーム処理・GUI 表示）

`RegisterPersistent` を使うと、ノード実行後もコールバックが毎フレーム呼ばれ続けます。  
永続実行中は WebUI に **PERSISTENT** バッジが表示されます。

```csharp
[NodeType("mymod.debug.fps_counter", "MyMod/Debug", "FPS Counter")]
[NodePort("fps", PortDirection.Output, "number")]
public sealed class FpsCounterNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        IPersistentRegistration? reg = null;
        float fps = 0f;

        reg = ctx.RegisterPersistent(new PersistentCallbacks
        {
            OnStart = () =>
            {
                // 登録後、最初の1回だけ呼ばれる（必ずメインスレッド）。ホスト固有 API を使う初期化はここ
                ctx.Logger.LogInfo("[FpsCounter] 開始しました");
            },
            OnUpdate = () =>
            {
                // ホストのメインスレッドで毎フレーム呼ばれる
                // ここではホスト固有 API を直接呼べる（以下は Unity の例）
                var type = Type.GetType("UnityEngine.Time, UnityEngine.CoreModule");
                var prop = type?.GetProperty("deltaTime");
                if (prop?.GetValue(null) is float dt && dt > 0)
                    fps = 1f / dt;

                ctx.SetPortValue("fps", (double)fps);
            },
            OnStop = () =>
            {
                // 停止時のクリーンアップ（IsActive が false になった時。Cancel() 経由でも発火）
                ctx.Logger.LogInfo("[FpsCounter] 停止しました");
            }
        });
    }
}
```

**`OnUpdate` のルール**:
- ホストのメインスレッドで実行されるため、ホスト固有 API を直接呼べる
- `MainThreadDispatch` は不要（すでにメインスレッド）
- `try-catch` は書いても機能しないことがある（一部の実行環境の制約）。クラッシュを避けるため null チェックを徹底する
- `Thread.Sleep` や `Task.Run` は呼ばない（メインスレッドをブロックするとホストがフリーズする）

**停止方法**: 右クリック → ⏹ Stop Persistent、または MCP の `stop_persistent_node`

---

## 8. ノード間共有 KV Store

`ctx.Store` でホストアプリケーション再起動後も永続する KV ストアにアクセスできます。複数のノード間でデータを共有する際に便利です。

```csharp
// 値を保存（型は string / long / double / bool / byte[] に対応）
ctx.Store.Set("mymod.lastTarget", "Player");

// 値を取得
var last = ctx.Store.Get<string>("mymod.lastTarget");

// 削除
ctx.Store.Delete("mymod.lastTarget");
```

キー名は他のノードと衝突しないよう `{プレフィックス}.{キー名}` 形式を推奨します。

### 発展的なトピック

以下は本ガイドでは扱いません。**リポジトリの `docs/node-developer-guide.md`** を参照してください。

- **ForEach ノード**（`IForEachController`）— リストの各要素で下流ノードを繰り返し実行する
- **PushLiveValue** — 断片実行を待たずに Snapshot をリアルタイム更新する
- **FragmentLink / 断片グラフ連携** — Snapshot 経由で断片間にデータを受け渡す仕組み
- **WebUI 拡張プラグイン**（`[NodeWebUi]` 属性）— `docs/webui-plugin-guide.md` を参照

---

## 9. DLL として配布する

`.cs` ホットリロードの代わりに、コンパイル済み DLL として配布することもできます。

### プロジェクト構成

```xml
<!-- MyCustomNodes.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>  <!-- 幅広い実行環境に対応 -->
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <!-- NodeGraphModLab.NodeAPI.dll のみ参照（ランタイム本体は不要） -->
    <Reference Include="NodeGraphModLab.NodeAPI">
      <HintPath>path\to\NodeGraphModLab.NodeAPI.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

### インストール方法

ビルドした `MyCustomNodes.dll` を以下に配置します：

```
ngolRoot/Nodes/CustomNodes/dll/MyCustomNodes.dll
```

次回ホストアプリケーション起動時に自動登録されます。

---

## 10. トラブルシューティング

### ノードが登録されない

- `[NodeType]` 属性が付いているか確認する
- `INode` インターフェースを実装しているか確認する
- 起動ログに `Registered dynamic node: {id}` が出ているか確認する
- コンパイルエラーがあればログにエラーが表示される

### ホットリロード後にノードが動かない

- ログの `Hot-reload failed:` を確認してエラーを修正する
- 成功した場合は `Hot-reloaded:` が出るまで確認してからテストする

### `static` フィールドの値が消える

ホットリロード時にアセンブリが置き換わるため、`static` フィールドは初期値にリセットされます。永続化が必要なデータは `ctx.Store`（KV Store）を使用してください。

### ホスト固有 API を `Execute()` で直接呼んでクラッシュする

`Execute()` はバックグラウンドスレッドで実行されます。ホスト固有 API は `MainThreadDispatch` でラップするか、`RegisterPersistent` の `OnUpdate` コールバック内で呼んでください。
