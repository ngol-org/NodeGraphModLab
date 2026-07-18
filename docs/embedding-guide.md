# NGOL 埋め込みガイド

このガイドは、自作の .NET ホストアプリケーションに NGOL を組み込む手順を説明します。
実際に動くサンプルは [`samples/NgolEmbedSample/`](../samples/NgolEmbedSample/)・
[`samples/NgolPluggableSample/`](../samples/NgolPluggableSample/) を参照してください。
本ガイドはその実装の要点を解説します。

## 1. ランタイム一式（`ngolRoot`）を用意する

NGOL は「実行時に読み込む DLL・WebUI 静的ファイル・ノードフォルダをひとまとめにしたディレクトリ」
（以下 `ngolRoot`）を前提に動作します。[GitHub Releases](../../releases) の
`NGOL-v{VERSION}.zip` を展開すると、次の構成の `NGOL/` フォルダが得られます。

```text
NGOL/
├── NodeGraphModLab.Core.dll
├── NodeGraphModLab.NodeAPI.dll
├── NodeGraphModLab.HostLogging.dll   ← 任意（無くても動作する。後述）
├── Microsoft.CodeAnalysis*.dll        ← Roslyn（カスタムノードのホットリロードに使用）
├── LiteDB.dll                         ← 共有 KV ストアの永続化バックエンド
├── Nodes/
│   ├── Builtin/NodeGraphModLab.BuiltinNodes.dll
│   └── CustomNodes/
│       ├── cs/                        ← ここに .cs を置くとホットリロードされる
│       └── dll/
├── WebUI/                             ← グラフエディタの静的ファイル
└── mcp/                                ← MCP サーバー（dist/index.js 済みバンドル）
```

ホストアプリケーションは、このフォルダをそのまま自身のバイナリと同じ場所（または任意の場所）に
配置し、そのパスを NGOL の起動処理に渡します。

## 2. 組み込み方式を選ぶ

コンパイル時参照の持ち方によって2通りの組み込み方式があります。

| 方式 | コンパイル時参照 | 適したケース |
|---|---|---|
| **直接参照**（[`NgolEmbedSample`](../samples/NgolEmbedSample/)） | `NodeGraphModLab.Core` 等への `<Reference>` を持つ（`Private=false` でコンパイル時型解決のみ） | NGOL を必須の依存として組み込みたい場合。コードが単純 |
| **任意の依存**（[`NgolPluggableSample`](../samples/NgolPluggableSample/)） | コンパイル時参照を一切持たない。実行時に `Assembly.LoadFrom` + reflection で起動 | NGOL の DLL が無くてもビルド・起動が成立してほしい場合（配布先で「あれば使う、無ければ使わない」任意の拡張にしたい） |

いずれの方式でも、起動処理の実体は1ファイル（`NgolActivator.cs`）にまとまっています。
既存プロジェクトへ組み込む場合は、対応するサンプルの `NgolActivator.cs` をコピーして
`TryStart(ngolRoot)` を1回呼ぶだけで完結します。

## 3. 起動処理の要点

直接参照版の要点（`NgolActivator.TryStart`）:

```csharp
var logger = new ConsoleFileNgolLogger(Path.Combine(AppContext.BaseDirectory, "host.log"));

var options = new NgolRuntimeOptions
{
    EnableDirectMode = true,   // フレーム駆動のUpdateコールバックが無いホスト向け。後述
    PluginVersion = "MyHostApp",
    GameName = "MyHostApp",    // WebUI接続時に表示されるホスト識別名
};

var runtime = new NgolRuntime(logger, options);
runtime.Initialize(ngolRoot);   // WebUIサーバー・ノードregistry・ホットリロード監視がここで起動する
```

`runtime`（`IDisposable`）を `Dispose()` すると NGOL を停止できます。

任意の依存版（`NgolPluggableSample`）は同じ処理を `Type.GetType` / `Activator.CreateInstance` /
`MethodInfo.Invoke` による reflection 経由で行います。NGOL の DLL が `ngolRoot` に存在しない場合は
何もせず `(null, null)` を返すため、呼び出し元は通常のアプリケーションとしてそのまま動作を継続できます。

### `EnableDirectMode` について

`EnableDirectMode = true` にすると、NGOL 内部で専用スレッドを立ててポーリングする動作になります。
コンソールアプリのようにフレーム駆動の `Update` コールバックを持たないホストではこちらを使います。
ゲームエンジンのようにフレーム駆動のコールバックを持つホストに埋め込む場合は `false` にし、
そのコールバックから NGOL のポンプ処理を呼び出す形にします（詳細は `NgolRuntimeOptions.cs` の
XML コメントを参照）。

## 4. ロガーについて

`NodeGraphModLab.HostLogging.dll`（コンソール + ファイル出力の `ConsoleFileNgolLogger`）は
任意同梱です。無い場合は `DispatchProxy` ベースの簡易コンソールロガーにフォールバックして
起動できます（`NgolPluggableSample` 参照）。すでにホスト側にロギング基盤がある場合は、
`INgolLogger` を実装したアダプタを自作して渡すこともできます。

## 5. WebUI・MCP への接続

起動後、コンソールに `WebUI: http://127.0.0.1:{port}/` が表示されます（既定ポート 11156、
`ngol-config.json` の `port` で変更可能）。ブラウザで開くとグラフエディタにアクセスできます。

MCP サーバー（`ngolRoot/mcp/`）は Node.js から `node mcp/dist/index.js` として起動し、
AI エージェント側の設定（`mcp.json` 等）から接続します。設定例は `mcp/mcp.json.example` を
参照してください。

## 6. カスタムノードの作成

`ngolRoot/Nodes/CustomNodes/cs/` に `.cs` ファイルを配置すると、ホストアプリケーションの
再起動なしに自動コンパイル・ノード登録されます（ホットリロード）。ノードの実装方法・
API リファレンスは [CUSTOM_NODE_GUIDE.md](../CUSTOM_NODE_GUIDE.md) を参照してください。
