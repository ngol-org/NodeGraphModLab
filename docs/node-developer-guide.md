# NodeGraphModLab — カスタムノード開発ガイド

**対象読者**: NodeGraphModLab にカスタムノードを追加したい開発者  
**前提知識**: C# の基礎知識

> 本ドキュメントは人間向けの完全版です。AI エージェント（MCP 経由）が参照する
> 圧縮版は `mcp/docs/node-dev-reference.md`（MCP ツール `get_node_dev_guide`）です。
> API を変更した場合は本ドキュメントを先に更新し、圧縮版へ反映してください。

---

## 目次

1. [概要と用語](#1-概要と用語)
2. [セットアップ](#2-セットアップ)
3. [最小構成のノード実装](#3-最小構成のノード実装)
4. [属性リファレンス](#4-属性リファレンス)
5. [IExecutionContext — 実行コンテキスト](#5-iexecutioncontext--実行コンテキスト)
6. [データ型一覧](#6-データ型一覧)
7. [Unity API を使うノード](#7-unity-api-を使うノード)
8. [永続コールバックノード](#8-永続コールバックノード)
9. [ForEach ノード（IForEachController）](#9-foreach-ノードiforeachcontroller)
10. [断片グラフ連携（Snapshot / PushLive）](#10-断片グラフ連携snapshot--pushlive)
11. [ノードのテスト](#11-ノードのテスト)
12. [配布・インストール](#12-配布インストール)
13. [トラブルシューティング](#13-トラブルシューティング)
14. [ノード間共有 KV Store（ctx.Store）](#14-ノード間共有-kv-storectxstore)
15. [WebUI カスタム UI（NodeWebUi 属性）](#15-webui-カスタム-uinodewebui-属性)

---

## 1. 概要と用語

NodeGraphModLab のノードは **`INode` インターフェースを実装した C# クラス** です。  
`[NodeType]`・`[NodePort]` 属性を付けると NodeRegistry が自動的に検出・登録します。

| 用語 | 説明 |
|---|---|
| **ノード** | グラフの処理単位。`INode.Execute()` が実行される |
| **ポート** | ノード間のデータ接続点。Input / Output の 2 種類 |
| **paramValues** | WebUI の Inspector で設定する固定パラメータ値 |
| **断片（Fragment）** | `fragmentLink` エッジで区切られた連結成分のサブグラフ |
| **Snapshot** | 断片間でデータを受け渡すための値保存機構 |

---

## 2. セットアップ

### 2.1 新規 C# プロジェクトを作成する場合

```xml
<!-- MyCustomNodes.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <Nullable>enable</Nullable>
    <Optimize>true</Optimize>
  </PropertyGroup>

  <ItemGroup>
    <!-- NodeGraphModLab.NodeAPI.dll のみ参照 -->
    <Reference Include="NodeGraphModLab.NodeAPI">
      <HintPath>path\to\NodeGraphModLab.NodeAPI.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

`NodeGraphModLab.NodeAPI.dll` は `ngolRoot/NodeGraphModLab.NodeAPI.dll` にあります。  
ランタイム本体（`NodeGraphModLab.Core.dll`）への参照は**不要**です。

### 2.2 既存プロジェクト (`NodeGraphModLab.BuiltinNodes`) に追加する場合

`NodeGraphModLab.BuiltinNodes/` 内の既存 `.cs` ファイルを参考に、同じフォルダに新規ファイルを追加するだけです。

### 2.3 動的コンパイル（Nodes/CustomNodes/cs/ フォルダ）

`ngolRoot/Nodes/CustomNodes/cs/` フォルダに `.cs` ファイルを置くと、ホストアプリケーション起動時および実行中（ホットリロード）に自動コンパイル・登録されます。  
WebUI の **Compile Node** パネルからもリアルタイムでコンパイル・登録できます。  
サブディレクトリも監視対象です（例: `cs/ai_generated/`）。

### 2.4 複数ファイル構成のノード（`.srclist`）

1つのノードを複数の `.cs` ファイルに分割し、ホットリロードを維持したまま開発できます。

`Foo.cs`（`[NodeType]` を持つノード本体）と同じフォルダ・同じベース名の **`Foo.srclist`** を置くと、そこに列挙したファイルが `Foo.cs` と一緒にコンパイルされます。

```
# Foo.srclist（1行1相対パス。パス基準は .srclist 自身のディレクトリ）
Foo.cs             # 基本形として自分自身も明示しておく（省略しても暗黙的に含まれる）
FooHelper.cs        # [NodeType] を持たない共有ロジック・データモデル等
../Common/Utils.cs  # フォルダを跨いだ相対パスでも指定可能
Widgets/            # 末尾 / でディレクトリを指定すると直下の *.cs のみを一括で含める（非再帰）
Vendor/**           # 末尾 /** でディレクトリを指定すると配下すべての *.cs を再帰的に一括で含める
```

- `#` から始まる行はコメント、空行は無視されます
- 末尾 `/` は直下のみ・末尾 `/**` はサブディレクトリを含めて再帰的に一括包含します。外部 OSS のソースツリーなど階層が深い構成を丸ごと取り込む場合は `/**` が有効です
- `FooHelper.cs` のような **`[NodeType]` を持たない共有ファイル**を更新すると、それを参照している依存ノードが自動でホットリロードされます（起動ログに `Shared file changed: FooHelper.cs — recompiling N dependent node(s)` と出力されます）
- `.srclist` が無いノードは従来通り単一ファイルとしてコンパイルされます（後方互換）

### 2.5 コンパイラオプション（`.rsp`）

`Foo.cs` と同じフォルダ・同じベース名の **`Foo.rsp`** を置くと、csc.exe 互換のコンパイラオプションを指定できます（本物の `Microsoft.CodeAnalysis.CSharp.CSharpCommandLineParser` でパースされます）。

```
# Foo.rsp
/r:SomeUtility.dll
/define:MY_FLAG
/nowarn:0219
/analyzer:SomeGenerator.dll
```

- `/r:`（追加参照アセンブリ）・`/define:`（プリプロセッサシンボル）・`/nowarn:` 等、csc.exe のレスポンスファイル構文がそのまま使えます
- **`/r:` のパスは `.rsp` ファイルの所在ディレクトリからの相対パス**です。ファイル名のみ（例: `/r:SomeUtility.dll`）のほか、サブフォルダ（例: `/r:lib/Jint.dll`）や親フォルダ（例: `/r:../shared/Jint.dll`）も指定できます（`RoslynCompiler.ParseRspFile()` が `Path.Combine(rspDir, refPath)` で解決）
- **コンパイル時参照と実行時ロードは別問題**です。`/r:` でコンパイルが通っても、実行時に `Assembly.Load` で参照 DLL が見つからないと `TypeLoadException` 等になり得ます。依存 DLL をサブフォルダに置く場合は、同一フォルダ直置きと同様に実行時解決が通ることを実機で確認してください
- `.rsp` にソースファイルを列挙する使い方は想定していません（ソースファイルの列挙は `.srclist` の役割）
- `AllowUnsafe` は `.rsp` の内容に関わらず常に有効です
- `/analyzer:` で事前ビルド済みの Roslyn Incremental Source Generator（`IIncrementalGenerator`）DLL を指定すると、本コンパイル前に実行され生成コードが合流します。クローンした OSS がコンパイル時コード生成に依存している場合に使用します。レガシー `ISourceGenerator` は非対応です

---

## 3. 最小構成のノード実装

```csharp
using NodeGraphModLab.NodeAPI;

[NodeType(
    "mymod.math.multiply",       // ノード ID（グローバル一意）
    "MyMod/Math",                // カテゴリパス（スラッシュ区切り）
    "Multiply",                  // WebUI 表示名
    Description = "2つの数値を乗算します")]
[NodePort("a",      PortDirection.Input,  "number", IsRequired = true,  Description = "第1オペランド")]
[NodePort("b",      PortDirection.Input,  "number", IsRequired = true,  Description = "第2オペランド")]
[NodePort("result", PortDirection.Output, "number", Description = "a × b")]
public sealed class MultiplyNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var a = Convert.ToDouble(ctx.GetPortValue("a"));
        var b = Convert.ToDouble(ctx.GetPortValue("b"));
        ctx.SetPortValue("result", a * b);
    }
}
```

**実行の流れ**:
1. 上流ノードが `SetPortValue("result", value)` を実行
2. 本ノードの `Execute()` が呼ばれる
3. `GetPortValue("a")` で値を取得 — 接続があればその値、無ければ Inspector / ノード本体のインライン欄で設定した `paramValues` を自動的に返す（string/number/bool のみ変換対応）
4. `SetPortValue("result", ...)` で下流ノードへ出力

---

## 4. 属性リファレンス

### NodeType 属性

```csharp
[NodeType(
    string nodeTypeId,       // 必須: グローバル一意 ID
    string category,         // 必須: カテゴリ（スラッシュ区切り階層）
    string displayName,      // 必須: WebUI 表示名
    Description = "...",     // 省略可: ツールチップ説明文
)]
```

#### ノード ID 命名規則

```
{名前空間}.{カテゴリ}.{ノード名}

例:
"ngol.logic.add"              ← 組み込みノード（ngol プレフィックス）
"mymod.category.node_name"    ← カスタムノード（独自プレフィックス推奨）
```

小文字スネークケースで記述します。既存の `ngol.*` ID との衝突を避けるため、独自のプレフィックスを使用してください。

### NodePort 属性

```csharp
[NodePort(
    string name,             // 必須: ポート名（英数字・アンダースコア）
    PortDirection direction, // 必須: Input / Output
    string dataType,         // 必須: データ型文字列（後述）
    IsRequired = false,      // Input のみ有効。true: 接続またはparamValues が必須
    ShowInlineEditor = false, // Input/Output 共通。true のときのみノード内インライン入力欄を表示（opt-in）
    Description = "..."      // 省略可: ツールチップ
)]
```

同一クラスに複数付与可。**Input ポートは上から順番に WebUI に表示されます**。

#### ShowInlineEditor について

WebUI のノードキャンバス表示は、デフォルトではポートの値をノード内に表示しません（Inspector パネルでのみ編集可能）。
`ShowInlineEditor = true` を指定したポートのみ、型がインライン対応であればノード内に直接入力欄が表示されます。

| 値 | 動作 |
|---|---|
| `null`（デフォルト・未指定） | ノード内インライン入力欄を非表示（Inspector でのみ編集） |
| `false` | 明示的に非表示（将来の拡張余地として維持） |
| `true` | ノード内にインライン入力欄を表示（型がインライン対応の場合のみ） |

インライン対応型: `boolean`/`bool`、`number`/`float`/`double`/`int`/`integer`、`string`、`enum:A|B|C`、`color`（スウォッチ表示のみ、編集は Inspector）。それ以外の型（`any`、`gameobject` 等）は `true` を指定してもラベルのみ表示されます。

出力ポートは `ShowInlineEditor = true` に加え、**そのノードが入力ポートを一つも持たない場合のみ**インライン入力欄が表示されます（定数ノードのように出力のみのノード向け。入力ポートを持つノードの出力側に紛らわしい入力欄を出さないための制約）。

```csharp
// 例: 定数ノードの出力値をノード内で直接編集可能にする
[NodePort("value", PortDirection.Output, "number", ShowInlineEditor = true)]

// 例: 検索キーのようによく手入力するポートを Input 側で直接編集可能にする
[NodePort("name", PortDirection.Input, "string", IsRequired = true, ShowInlineEditor = true)]
```

Inspector パネルでは `ShowInlineEditor` の値に関わらず、常にすべてのポートを編集できます。

#### 予約ポート `__exec_in__` / `__exec_out__` について

WebUI は全ノードに、`[NodePort]` の宣言に関わらず常時 2 つの合成ポート
（実行順序専用ハンドル）を描画します。ノード実装者側で何かを宣言する必要はありません。
データを運ばず、トポロジカルソート上の実行順序（依存関係）のみを表します。
詳細は `graph-spec.md` §2.3 を参照してください。

---

## 5. IExecutionContext — 実行コンテキスト

```csharp
public interface IExecutionContext
{
    // ノード識別子
    string NodeInstanceId { get; }

    // ログ出力
    INodeLogger Logger { get; }

    // ポート値の読み書き
    object? GetPortValue(string portName);
    void SetPortValue(string portName, object? value);

    // Inspector の固定パラメータ値
    T? GetParam<T>(string paramName);

    // Unity API 呼び出し（メインスレッドへのキュー）
    void MainThreadDispatch(Action action);

    // 永続コールバック登録（PersistentCallbacks を渡す）
    IPersistentRegistration RegisterPersistent(PersistentCallbacks callbacks);

    // 断片グラフ連携
    ISnapshotStore? SnapshotStore { get; }
    IReadOnlyList<PortConnection> GetDownstreamConnections(string outputPortName);
    void PushLiveValue(string portName, object? value);

    // WebUI pushLiveParams が書き込むライブパラメータ（永続ノードの onUpdate で opt-in 読み取り）
    T GetLiveParam<T>(string key, T defaultValue = default);

    // ノード間共有 KV Store（ゲーム再起動後も永続）
    IKVStore Store { get; }
}
```

### 値取得のパターン

`ctx.GetPortValue(name)` は「ポート接続 → なければ paramValues（Inspector / インライン欄の固定値）」を**自動的にフォールバック**します。string / number(`double`) / bool のプリミティブ型はこれだけで完結します。

```csharp
// パターン1: 数値・文字列・真偽値ポート — GetPortValue だけで接続/固定値の両方に対応
var value = Convert.ToDouble(ctx.GetPortValue("input") ?? 0.0);

// パターン2: 型チェック付き取得（gameObject 等の参照型はそもそも固定値化できないため接続必須）
if (ctx.GetPortValue("gameObject") is { } obj)
{
    // obj を使用
}

// パターン3: object/array（JSON オブジェクト・配列）を固定値として受け取りたい場合のみ
// GetPortValue は構造化型を変換できないため ctx.GetParam<T> を明示的に使う
var items = ctx.GetParam<List<string>>("items");
```

> **`ctx.GetParam<T>` が必要なケース**: 構造化型（`Color` / `Vector3` など）を paramValues から直接デシリアライズしたい場合のみ。プリミティブ型では `GetPortValue` だけで十分なので、`?? ctx.GetParam<T>(name)` を書く必要はない。
>
> **`double`/`float`/`int` 等の非nullable値型では `GetParam<T>(...) ?? リテラル` は書かない**: `GetParam<T>` は `T? GetParam<T>(string)` という非制約ジェネリックで宣言されており、`T` が非nullable値型の場合 `T?` は `Nullable<T>` ではなく素の `T` に解決される。そのため `??` の左辺が非nullable値型になりコンパイルエラー（CS0019）になる。未設定時は元々 `default(T)` を返すため、`?? リテラル` 自体が不要（`string` 等の参照型では `T?` が正しく nullable になるため問題ない）。

### ログ出力

```csharp
ctx.Logger.LogInfo($"[MyNode] 処理完了: {result}");
ctx.Logger.LogWarning("[MyNode] 入力が null です");
ctx.Logger.LogError("[MyNode] 予期しないエラー");
ctx.Logger.LogDebug("[MyNode] デバッグ情報");
```

ホストのログおよび WebUI の実行ログパネルに表示されます。

---

## 6. データ型一覧

### 重要な前提: ポート型は実行時にチェックされない

DataType 文字列は **WebUI の表示・色分け・接続ガイド用のメタデータ** です。  
C# の型システムとは独立しており、フレームワークは実行時に型チェックを一切行いません。  
型安全性はノード実装者が自前で担保する必要があります（`is`/`as`/`Convert` 等）。

すべてのポート値は `object?` として受け渡されます。値型（struct）は Boxing/Unboxing が発生します。

### 型文字列一覧

| dataType 文字列 | C# 実体（代表例） | 備考 |
|---|---|---|
| `"any"` | `object?` | 任意の型を受け入れる慣例型 |
| `"number"` | `double` / `float` / `int` | 数値全般。Boxing あり |
| `"string"` | `string` | 参照型、Boxing なし |
| `"bool"` | `bool` | 真偽値。Boxing あり |
| `"list"` | `IEnumerable<object?>` | `SnapshotListNode` は `List<object?>` に materialize |
| `"gameobject"` | `object` (IL2CPP 経由) | Snapshot 等で使う小文字形式 |
| `"transform"` | `object` (IL2CPP 経由) | |
| `"component"` | `object` (IL2CPP 経由) | 汎用コンポーネント |
| `"rigidbody"` | `object` (IL2CPP 経由) | |
| `"collider"` | `object` (IL2CPP 経由) | |
| `"material"` | `object` (IL2CPP 経由) | |
| `"vector2"` | `object` (struct Boxing あり) | |
| `"vector3"` | `object` (struct Boxing あり) | |
| `"vector4"` | `object` (struct Boxing あり) | |
| `"color"` | `object` (struct Boxing あり) | |
| `"quaternion"` | `object` (struct Boxing あり) | |
| `"rect"` | `object` (struct Boxing あり) | |
| `"bounds"` | `object` (struct Boxing あり) | |

> **DataType 文字列の大文字・小文字**: すべての組み込みノードは小文字形式（`"gameobject"`, `"transform"` 等）で統一されています。カスタムノードでも小文字形式を使用してください。フレームワークは同一視しません。

### struct 型の安全な Unboxing パターン

```csharp
// Vector3 の場合（IL2CPP では object? として来る）
var raw = ctx.GetPortValue("vector3");
if (raw == null) return;

// 安全な取得（型名でリフレクション）
var posType = raw.GetType();
var x = (float)(posType.GetField("x")?.GetValue(raw) ?? 0f);
var y = (float)(posType.GetField("y")?.GetValue(raw) ?? 0f);
var z = (float)(posType.GetField("z")?.GetValue(raw) ?? 0f);
```

---

## 7. Unity API を使うノード

IL2CPP コンパイルされた Unity では型参照がリフレクション経由になります。

### 基本パターン

```csharp
[NodeType("mymod.unity.set_scale", "MyMod/Unity", "Set Scale")]
[NodePort("gameObject", PortDirection.Input,  "GameObject", IsRequired = true)]
[NodePort("scale",      PortDirection.Input,  "number",     IsRequired = true)]
public sealed class SetScaleNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var go = ctx.GetPortValue("gameObject");
        var scale = Convert.ToSingle(ctx.GetPortValue("scale") ?? ctx.GetParam<float>("scale"));

        if (go == null) { ctx.Logger.LogWarning("[SetScale] gameObject が null"); return; }

        var done = new ManualResetEventSlim(false);
        ctx.MainThreadDispatch(() =>
        {
            try
            {
                // Transform プロパティ取得
                var goType = go.GetType();
                var transform = goType.GetProperty("transform")?.GetValue(go);

                // localScale に Vector3 を設定
                var transformType = transform?.GetType();
                var v3Type = Type.GetType("UnityEngine.Vector3, UnityEngine.CoreModule");
                var v3 = Activator.CreateInstance(v3Type!, scale, scale, scale);
                transformType?.GetProperty("localScale")?.SetValue(transform, v3);
            }
            finally { done.Set(); }
        });
        done.Wait(TimeSpan.FromSeconds(2));
    }
}
```

### IL2CPP でよく使うリフレクションパターン

```csharp
// GameObject.Find
var goType = Type.GetType("UnityEngine.GameObject, UnityEngine.CoreModule");
var found = goType?.GetMethod("Find", new[] { typeof(string) })?.Invoke(null, new object[] { "Name" });

// GetComponent
var comp = goType?.GetMethod("GetComponent", new[] { typeof(string) })
               ?.Invoke(go, new object[] { "MeshRenderer" });

// プロパティ読み取り
var pos = Type.GetType("UnityEngine.Transform, UnityEngine.CoreModule")
              ?.GetProperty("position")?.GetValue(transform);
```

### MainThreadDispatch の重要な注意点

- Unity API は **必ずメインスレッドから** 呼び出すこと
- `MainThreadDispatch` は Unity の `Update()` タイミングで実行される
- `done.Wait(TimeSpan.FromSeconds(2))` でタイムアウトを設ける
- `try/finally` で必ず `done.Set()` を呼ぶ（デッドロック防止）

### 永続コールバックのホスト拡張パターン（例: Unity の OnGUI）

`RegisterPersistent`（詳細は[§8](#8-永続コールバックノード)）が受け取る `PersistentCallbacks` 自体は
ホスト非依存の `OnUpdate` / `OnStart` / `OnStop` しか持たない。Unity の `OnGUI` / `FixedUpdate` /
`LateUpdate` のようなホスト固有フェーズが必要な場合、ホストブリッジ側が `PersistentCallbacks` を
継承し `GetPhase(string phaseName)` をオーバーライドして提供する（例: Unity ホスト統合層の
`UnityPersistentCallbacks` が `"Unity.OnGUI"` などのフェーズ名を解決する）。ノード側はホストが
提供するサブクラスを使う:

```csharp
var reg = ctx.RegisterPersistent(new UnityPersistentCallbacks // ホストブリッジ提供の PersistentCallbacks 派生
{
    OnUpdate = () => st.OnUpdate(),
    OnGUI    = () => st.OnGUI(),
});
```

### 独自 MonoBehaviour を使うノード（IL2CPP 対応）

> **⚠️ 開発中ノードでは現在非推奨 **  
> `ClassInjector.RegisterTypeInIl2Cpp<T>()` は IL2CPP の静的キャッシュに型を登録するため、ホットリロード時に `TypeInitializationException` が発生する。  
> 解決されるまでは、開発中の IL2CPP カスタムノードでは本パターンを使わず、**`RegisterPersistent` + `onUpdate`/`onGui` コールバック** を使うこと。  
> ゲームへのリリース済みノード（ホットリロード不要）や 解決後は、本パターンが推奨に戻る。

`RegisterPersistent` では受け取れない Unity イベント（`OnAnimatorIK` など）や、  
特定の GameObject に直接アタッチして動作させたいケースでは、  
**ノード内で MonoBehaviour サブクラスを定義して AddComponent する**方式が使えます。

Roslyn（.cs ホットリロード）ノードを含め、IL2CPP ゲームでも動作することが確認済みです（2026-05-20 検証）。

#### IL2CPP での必須手順

IL2CPP ゲームでは `AddComponent<T>()` の前に **`ClassInjector.RegisterTypeInIl2Cpp<T>()`** を一度呼ぶ必要があります。

```csharp
#nullable disable
using System;
using Il2CppInterop.Runtime.Injection;   // ClassInjector
using NodeGraphModLab.NodeAPI;
using UnityEngine;

[NodeType("mymod.anim.ik_hook", "MyMod/Anim", "IK Hook")]
public sealed class IkHookNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var targetGo = ctx.GetPortValue("target") as GameObject;
        if (targetGo == null) return;

        ctx.MainThreadDispatch(() =>
        {
            // IL2CPP: 初回のみ型を登録
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<MyIkHook>())
                ClassInjector.RegisterTypeInIl2Cpp<MyIkHook>();

            // 対象 GO に既存コンポーネントがあれば再利用、なければ追加
            var hook = targetGo.GetComponent<MyIkHook>()
                       ?? targetGo.AddComponent<MyIkHook>();
            hook.IkCallback = ApplyIk;
        });
    }

    private static void ApplyIk(Animator anim, int layer) { /* IK 処理 */ }
}

public class MyIkHook : MonoBehaviour
{
    public Action<Animator, int> IkCallback;

    // IL2CPP 必須コンストラクタ
#if NET6_0_OR_GREATER
    public MyIkHook(IntPtr ptr) : base(ptr) { }
#endif

    private void OnAnimatorIK(int layerIndex)
    {
        // ここでは transform / GetComponent など Unity メソッドが普通に使える
        var anim = GetComponent<Animator>();
        IkCallback?.Invoke(anim, layerIndex);
    }
}
```

#### 注意点

- **`IntPtr` コンストラクタ必須**: `#if NET6_0_OR_GREATER` ガードで囲む。ないと AddComponent 時にクラッシュ。
- **`RegisterTypeInIl2Cpp` は AddComponent より前**: 呼ばないと `TypeInitializationException`。
- **二重登録は `IsTypeRegisteredInIl2Cpp` で防ぐ**: 複数回の Execute でも安全。
- **Mono ゲームでは不要**: `#if NET6_0_OR_GREATER` ブロックに閉じ込めるか、実行時の判定で分岐する。
- **`Il2CppInterop.Runtime.Injection` は Roslyn から参照可能**: ホスト環境同梱の IL2CPP 関連 DLL がアセンブリ検索パスに含まれている場合。

### RegisterPersistent vs 独自 MonoBehaviour の使い分け

| 手段 | 向いているケース |
|---|---|
| `RegisterPersistent`（[§8](#8-永続コールバックノード)） | Update（汎用）+ ホストブリッジが提供する拡張フェーズ（Unity なら LateUpdate / FixedUpdate / OnGUI 等）。特定 GO 不要。ライフサイクル管理が自動 |
| **独自 MonoBehaviour + AddComponent** | 特定の GO（VRM アバター等）に紐づくイベント。`OnAnimatorIK` / `OnAnimatorMove` など NodeGraphModLabComponent が転送しないイベント全般 |

---

## 8. 永続コールバックノード

ホストのフレーム更新（Unity なら `Update()` 等）に継続的なコールバックを登録するノードです。  
Inspector ノードのようにゲームオブジェクトの状態を毎フレーム監視する場合に使用します。

登録 API は `ctx.RegisterPersistent(new PersistentCallbacks { ... })` です（`PersistentCallbacks` は `NodeGraphModLab.NodeAPI`）。  
`PersistentCallbacks` 自体が持つのはホスト非依存の `OnUpdate` / `OnStart` / `OnStop` のみ。  
コールバック本体は状態クラスに書き、**1 行で委譲**するのが読みやすい（`samples/CustomNodes/SubCameraStreamNode.cs` 参照）。

```csharp
var st = new MyState(/* ... */);
var reg = ctx.RegisterPersistent(new PersistentCallbacks
{
    OnUpdate = () => st.OnUpdate(),
});
st.SetRegistration(reg, ctx);
```

**ホスト固有フェーズ（Unity の `OnGUI` / `FixedUpdate` / `LateUpdate` 等）が必要な場合**は、
ホストブリッジ提供のサブクラス経由で拡張する。具体例と `GetPhase(string)` の仕組みは
[§7「永続コールバックのホスト拡張パターン」](#7-unity-api-を使うノード)を参照。

### ライフサイクルコールバック（OnStart / OnStop）

`PersistentCallbacks` には毎フレーム呼ばれる `OnUpdate` とは別に、
NGOL の登録ライフサイクルに連動する 2 つのイベントがあります。

| コールバック | 発火タイミング |
|---|---|
| `OnStart` | 登録後、最初の Drain 呼び出しで 1 回だけ対象コールバックより前に呼ばれる。必ずホストのメインスレッドから呼ばれるため、Unity API 等ホスト固有 API を使う初期化処理はここに置く（`Execute()` は背景スレッドから呼ばれる場合があるため） |
| `OnStop` | 登録が停止されるタイミングで呼ばれる。WebSocket・スレッド・GameObject 等のリソース解放に使用する。**`Cancel()` 呼び出し経由でも発火する**（次の Drain で 1 回だけ、二重呼び出し防止済み） |

```csharp
ctx.RegisterPersistent(new PersistentCallbacks
{
    OnStart  = () => { /* ホスト固有 API を使う初期化 */ },
    OnUpdate = () => { /* 毎フレーム */ },
    OnStop   = () => { /* リソース解放 */ },
});
```

### GC 負荷への注意

```csharp
[NodeType("mymod.unity.position_watcher", "MyMod/Unity", "Position Watcher",
    Description = "GameObject の位置が変化したときのみ Snapshot に push します")]
[NodePort("gameObject", PortDirection.Input,  "GameObject", IsRequired = true)]
[NodePort("x",          PortDirection.Output, "number")]
[NodePort("y",          PortDirection.Output, "number")]
[NodePort("z",          PortDirection.Output, "number")]
public sealed class PositionWatcherNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var go = ctx.GetPortValue("gameObject");
        if (go == null) return;

        // 直前フレームの値をクロージャで保持（変化検知用）
        float prevX = float.NaN, prevY = float.NaN, prevZ = float.NaN;

        ctx.RegisterPersistent(new PersistentCallbacks
        {
            OnUpdate = () =>
            {
                // 毎フレーム呼ばれる（メインスレッド）
                var transform = go.GetType().GetProperty("transform")?.GetValue(go);
                if (transform == null) return;
                var pos = transform.GetType().GetProperty("position")?.GetValue(transform);
                if (pos == null) return;

                var posType = pos.GetType();
                var x = (float)(posType.GetField("x")?.GetValue(pos) ?? 0f);
                var y = (float)(posType.GetField("y")?.GetValue(pos) ?? 0f);
                var z = (float)(posType.GetField("z")?.GetValue(pos) ?? 0f);

                // 変化がなければ送出しない（ポート送出によるBoxing/GC 負荷を軽減）
                if (x == prevX && y == prevY && z == prevZ) return;

                prevX = x; prevY = y; prevZ = z;

                ctx.PushLiveValue("x", (double)x);
                ctx.PushLiveValue("y", (double)y);
                ctx.PushLiveValue("z", (double)z);
            },
        });
    }
}
```

**PushLiveValue の動作**:  
`ISnapshotStore.PushLive()` を呼び出し、WebUI の Snapshot パネルをリアルタイム更新します。  
断片グラフの `fragmentLink` 経由で下流断片の入力に値を注入するために使用します。

**GetLiveParam の動作**:  
WebUI の `NGOL.pushLiveParams(nodeInstanceId, params)` が `LiveParamStore` に書き込んだ値を読みます。
`RegisterPersistent` の `onUpdate` 内で毎フレーム呼ぶ想定です。ノードが呼ばない限り影響はありません。
`setSnapshotValue` とは別ストア（SnapshotStore ではなく LiveParamStore）です。

```csharp
onUpdate = () =>
{
    var scale = ctx.GetLiveParam("scale", 1.0);
    // スライダーで pushLiveParams された scale を適用
},
```

**IPersistentRegistration の返り値**:  
`Dispose()` を呼ぶとコールバック登録が解除されます。通常はノード実行が停止するまで有効です。

---

## 9. ForEach ノード（IForEachController）

リストを反復処理するノードは `INode` と `IForEachController` の両方を実装します。

```csharp
[NodeType("mymod.logic.my_foreach", "MyMod/Logic", "My ForEach")]
[NodePort("items",        PortDirection.Input,  "list", IsRequired = true)]
[NodePort("current_item", PortDirection.Output, "any")]
[NodePort("index",        PortDirection.Output, "number")]
public sealed class MyForEachNode : INode, IForEachController
{
    // IForEachController
    public string CurrentItemPortName => "current_item";
    public string IndexPortName       => "index";

    public IReadOnlyList<object?> GetItems(IReadOnlyDictionary<string, object?> inputValues)
    {
        if (!inputValues.TryGetValue("items", out var raw)) return [];
        return raw is IEnumerable<object?> list ? list.ToList() : [];
    }

    // INode（Execute は GraphExecutor から呼ばれるが、ループ制御は IForEachController が担う）
    public void Execute(IExecutionContext ctx) { }
}
```

`GraphExecutor` は `IForEachController` を検出すると、`GetItems()` の各要素に対して  
`CurrentItemPortName` のポートに値をセットしながら下流ノードを繰り返し実行します。

**制約**: 1 グラフに ForEach ノードは 1 個のみサポートしています。

---

## 10. 断片グラフ連携（Snapshot / PushLive）

### Snapshot ノードとして実装する

断片間でデータを受け渡す **Snapshot ノード** を実装する場合:

```csharp
[NodeType("mymod.snapshot.my_type", "MyMod/Snapshot", "Snapshot (MyType)")]
[NodePort("value",  PortDirection.Input,  "any")]
[NodePort("value",  PortDirection.Output, "any")]
public sealed class MySnapshotNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var value = ctx.GetPortValue("value");

        // 断片実行時のみ SnapshotStore が non-null
        if (ctx.SnapshotStore != null && value != null)
        {
            ctx.SnapshotStore.SetSnapshot(ctx.NodeInstanceId, "value", value);
        }

        ctx.SetPortValue("value", value);
    }
}
```

`ctx.SnapshotStore` は断片実行（`execute_fragment` / `execute_all_fragments`）時のみ非 null です。  
グラフ全体実行（`execute_graph`）では null になります。

### 断片連携フローの概要

```
断片A実行
  Snapshot ノード: SnapshotStore.SetSnapshot("node-1", "value", someObject)
       ↓
  WebUI が fragmentLink 経由で断片B の入力に注入
       ↓
断片B実行
  B の入力ノードに上流 Snapshot 値が paramValues として渡される
```

---

## 11. ノードのテスト

`TestExecutionContext` を使って Unity なしで単体テストが書けます。

```csharp
// NodeGraphModLab.Tests プロジェクトでの例
using NodeGraphModLab.NodeAPI;
using NUnit.Framework;

[TestFixture]
public class MultiplyNodeTests
{
    [Test]
    public void Execute_TwoNumbers_ReturnsProduct()
    {
        var node = new MultiplyNode();
        var ctx = new TestExecutionContext("node-1");
        ctx.SetInput("a", 3.0);
        ctx.SetInput("b", 4.0);

        node.Execute(ctx);

        Assert.That(ctx.GetOutput("result"), Is.EqualTo(12.0));
    }
}
```

`TestExecutionContext` は `NodeGraphModLab.Tests` プロジェクト内に定義されています。  
カスタムノードプロジェクトでテストを書く場合は、同様のモック実装を作成してください。

---

## 12. 配布・インストール

### DLL として配布する場合

1. プロジェクトをビルドして `MyCustomNodes.dll` を作成
2. `ngolRoot/` フォルダに DLL を配置
3. ホストアプリケーションを起動 → NodeGraphModLab が自動スキャンして登録

### Nodes/CustomNodes/cs/ フォルダで配布する場合

1. `.cs` ファイルを `ngolRoot/Nodes/CustomNodes/cs/` に配置
2. ホストアプリケーション起動時に自動コンパイル・登録。実行中の配置ならホットリロード（500ms debounce）で即時反映

#### スキャン対象フォルダ

```
ngolRoot/
├── NodeGraphModLab.NodeAPI.dll     ← 必須（プリインストール済み）
├── NodeGraphModLab.BuiltinNodes.dll             ← 組み込みノード
├── MyCustomNodes.dll               ← ← ここに配置
├── Nodes/                         ← .cs ファイルを置くとホットリロード
│   └── MyNode.cs
└── dynamic-nodes/                  ← Roslyn コンパイル済みキャッシュ（自動生成）
```

### バージョン互換性

カスタムノードは `NodeGraphModLab.NodeAPI.dll`（`INode`/`IExecutionContext`/属性など）に対してコンパイルする。
気にすべきはこの **NodeAPI バージョン**（`NodeGraphModLab.NodeAPI.csproj` の `<Version>`）だけで、
ランタイム本体・WebUI のバージョン（Product Version）が上がってもノード側の対応は基本不要。

- NodeAPI の MINOR バージョンが上がった場合のみ、公開APIの追加/変更がある可能性があるため
  `CHANGELOG.md` の `### Breaking` 記載を確認し、必要なら再コンパイルする。
- PATCH のみの更新（NodeAPI 側の内部修正）はソース互換に影響しない。
- バージョン方針全体は [versioning-policy.md](versioning-policy.md) を参照。

---

## 13. トラブルシューティング

### ノードが WebUI に表示されない

- `[NodeType]` 属性の第1引数（ノード ID）が他と重複していないか確認
- DLL が正しいフォルダに配置されているか確認（`ngolRoot/` 直下）
- 起動ログでスキャンエラーがないか確認

### Execute() が呼ばれない

- トポロジカルソートの依存関係を確認（孤立ノードは実行されない場合あり）
- Required ポートへの接続が不足していると実行がスキップされる場合がある

### Unity API を呼ぶと NullReferenceException が発生する

- `MainThreadDispatch` の外から Unity API を呼んでいないか確認
- `Type.GetType("UnityEngine.XXX, AssemblyName")` のアセンブリ名が正しいか確認
  - CoreModule: `UnityEngine.CoreModule`
  - 等

### PushLiveValue が WebUI に反映されない

- 永続ノード（`RegisterPersistent`）の `OnUpdate`（または `GetPhase` 経由のホスト拡張コールバック）内から呼んでいるか確認
- `ctx.SnapshotStore` が null でないか確認（断片実行時のみ非 null）

---

## 14. ノード間共有 KV Store（ctx.Store）

`ctx.Store` はすべてのノードから同一インスタンスにアクセスできる **永続 Key-Value ストア** です。  
ゲームを再起動しても値が保持されます（LiteDB バックエンドで `kvstore.db` に永続化）。

### 主な用途

| ユースケース | 具体例 |
|---|---|
| ゲーム定数・設定値の共有 | キャラクター ID テーブル、設定フラグ |
| 初回解析結果のキャッシュ | `get_method_ptr` の結果を保存 → 再起動後に再取得不要 |
| ノード間のセッションをまたぐ状態共有 | `AppDomain.SetData/GetData` の公式代替 |

### 基本 API

```csharp
public interface IKVStore
{
    void Set(string key, object? value);
    object? Get(string key);
    T? Get<T>(string key);
    bool TryGet<T>(string key, out T? value);
    bool ContainsKey(string key);
    void Delete(string key);
    IEnumerable<string> Keys(string? prefix = null);   // null = 全件
}
```

### 使用例

```csharp
using NodeGraphModLab.NodeAPI;

// --- 値の書き込み（永続化）---
ctx.Store.Set("mymod.config.debug", true);
ctx.Store.Set("mymod.rva.swap_fn", 0x1a253e0L);

// --- 型付き読み取り ---
var debug = ctx.Store.Get<bool>("mymod.config.debug");   // true
var rva   = ctx.Store.Get<long>("mymod.rva.swap_fn");    // 0x1a253e0

// --- 存在しないキーは default を返す ---
var missing = ctx.Store.Get<string>("mymod.not_exist");  // null

// --- TryGet パターン ---
if (ctx.Store.TryGet<long>("mymod.rva.swap_fn", out var cachedRva))
{
    // キャッシュヒット: 再解析不要
}

// --- プレフィックスでキー一覧取得 ---
var allKeys   = ctx.Store.Keys();              // 全件
var mymodKeys = ctx.Store.Keys("mymod.");      // "mymod." で始まるキーのみ

// --- 削除 ---
ctx.Store.Delete("mymod.temp.value");
```

### 初回のみ処理するパターン（解析結果キャッシュ）

```csharp
[NodeType("mymod.init.setup", "MyMod/Init", "Setup (run once)")]
[NodePort("result", PortDirection.Output, "string")]
public class SetupNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        if (ctx.Store.ContainsKey("mymod.initialized"))
        {
            ctx.SetPortValue("result", "already initialized");
            return;
        }

        // 重い初期化処理（初回のみ）
        ctx.Store.Set("mymod.config.threshold", 100);
        ctx.Store.Set("mymod.initialized", true);
        ctx.SetPortValue("result", "initialized");
    }
}
```

### 注意事項

| 事項 | 詳細 |
|---|---|
| **スレッド安全** | 読み書きはスレッドセーフ（`ConcurrentDictionary` ベース） |
| **型の保持** | 再起動後の復元は JSON デシリアライズ経由。`string`・数値型は確実に復元される |
| **`IntPtr` は永続化不可** | ホストアプリケーション再起動でアドレスが変わる。関数ポインタは毎起動時に再解決すること |
| **キー命名規則** | `{prefix}.{category}.{name}` 形式を推奨。他の拡張や NGOL 本体との衝突を避けること |
| **保存先** | `ngolRoot/kvstore.db`（LiteDB）。起動失敗時は `.json` にフォールバック |

---

## 15. WebUI カスタム UI（NodeWebUi 属性）

ノードに WebUI 側のカスタム UI（ウィジェット / フルノード描画）を紐付けられます。
UI 本体は **WebUI プラグイン JS** として別途配布し、C# 側はプラグイン ID を宣言するだけです。

```csharp
[NodeType("my.gauge_probe", "MyNodes", "Gauge Probe")]
[NodePort("value", PortDirection.Input, "number")]
[NodeWebUi("my.webui.gauge",              // WebUI プラグイン ID
    OptionsFromSnapshot = "value",        // 任意: スナップショット参照ポート名
    BindTo = "selected",                  // 任意: UI 選択値の書き込み先パラメータ名
    ExtraJson = "{\"max\":\"100\"}")]     // 任意: プラグイン固有設定（有効な JSON オブジェクト）
public sealed class GaugeProbeNode : INode { ... }
```

| プロパティ | 必須 | 説明 |
|---|---|---|
| `PluginId`（コンストラクタ引数） | ✅ | JS 側 `registerWidget` / `registerNodeRenderer` の ID と一致させる |
| `OptionsFromSnapshot` | — | UI がスナップショット値を読むポート名（list item selector / gauge 系） |
| `BindTo` | — | UI の選択・入力値を書き込む paramValues キー名 |
| `ExtraJson` | — | プラグイン固有設定。WebUI には `spec.extra` として渡る。**不正な JSON の場合 UI は標準表示にフォールバック** |

ポイント:

- **`.cs` と `.js` はペアで配布する**。C# はノード型・ポート・実行を担当し、JS は描画のみを担当する
- UI がウィジェット（ノード枠内の 1 区画）になるかフルノード描画になるかは、
  **JS 側がどちらの API で登録したかで決まる**（C# 側にモード指定はない）
- 対応プラグイン未インストール環境では標準テキスト表示で動作継続する（graceful degradation）
- UI から書き込まれた値は `ctx.GetParam<string>("...")` で受け取る（文字列で届く想定で
  `double.TryParse(..., CultureInfo.InvariantCulture, ...)` 等の防御的パースを推奨）
- スナップショット連携する場合は `ctx.SnapshotStore?.SetSnapshot(ctx.NodeInstanceId, "<port>", 値)` を
  **UI が参照する全ポート分**呼ぶこと（呼んだポートの snapshot_saved が WebUI へ push される）

JS プラグインの作り方・`window.NGOL` API・サンプルは **[webui-plugin-guide.md](webui-plugin-guide.md)** を参照。

> **第三者プラグインによる UI 上書きについて**
> WebUI プラグインは、あなたのノードが `[NodeWebUi]` を宣言していなくても、ノード型 ID を指定して
> UI を上書きできます（webui-plugin-guide.md §3.8）。上書き適用中はノードに **Override バッジ**が
> 表示され、ユーザーは Plugins メニュー > Node Overrides からいつでも標準 UI に戻せます。
> **「ノードの表示・入力がおかしい」という報告を受けたら、まず Override バッジの有無を確認
> してもらってください**（原因があなたのノードではなく第三者の上書き UI である可能性があります）。

---

*このドキュメントは NodeGraphModLab v0.7.1 を対象としています。*
