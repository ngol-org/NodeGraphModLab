# NgolEmbedSample

**自作の .NET アプリへ NGOL を通常のライブラリとして組み込む**最小サンプルです。
参照を追加して起動処理へ数行足すだけで、WebUI・MCP・ノードのホットリロードが立ち上がります。

NGOL を「あれば使う、無ければ使わない」という任意の依存として組み込みたい場合は、
reflection ベースの [`samples/NgolPluggableSample/`](../NgolPluggableSample/) を参照してください。

## 組み込みに必要なもの

`Program.cs` の中身がそのまま答えです。

```csharp
var ngolRoot = Path.Combine(AppContext.BaseDirectory, "ngol-resources");

var logger = new ConsoleFileNgolLogger(Path.Combine(AppContext.BaseDirectory, "host.log"));
var options = new NgolRuntimeOptions { EnableDirectMode = true, PluginVersion = "MyApp", GameName = "MyApp" };
var runtime = new NgolRuntime(logger, options);
runtime.Initialize(ngolRoot);

// アプリ終了時
runtime.Dispose();
```

`EnableDirectMode = true` のとき、NGOL は内部スレッドを立てて自身を駆動します。
自前のループを持つホストでは `false` にし、そのループから `runtime.Tick()` を毎回呼んでください。

## 構成

| ファイル | 役割 |
|---|---|
| `Program.cs` | エントリポイント。NGOL の起動・停止はここの数行だけ |
| `NgolEmbedSample.csproj` | net6.0 コンソールアプリ。NGOL を通常の参照として取り込み、`ngol-resources/` をビルド出力へコピーする |
| `ngol-config.json` | port 11156 既定 |
| `setup-ngol-embed-sample.ps1` | リリースzipの `NGOL/` から `ngol-resources/`（NGOLリソース）を組み立てる |

### DLL と NGOLリソースの置き場所

| | 置き場所 | 誰が配置するか |
|---|---|---|
| NGOL 本体の DLL | 実行ファイルと同じフォルダ | **ビルド**（通常のライブラリ参照なので自動で同梱される） |
| NGOLリソース（`Nodes/` `WebUI/` `ngol-config.json`） | 実行ファイルの隣の `ngol-resources/` | `setup-*.ps1` が組み立て、**ビルドが出力へコピー** |

`ngol-resources/` に DLL は入りません。ビルド時に参照した DLL がそのまま実行時にも使われるため、
版が食い違うことがありません。

## 使い方

1. リリースzipを展開し、`NGOL/` フォルダのパスを確認する
2. NGOLリソースを組み立てる:
   ```powershell
   cd samples\NgolEmbedSample
   .\setup-ngol-embed-sample.ps1 -SourceDir "<展開したNGOL/フォルダのパス>"
   ```
3. 起動する:
   ```powershell
   dotnet run
   ```
4. コンソールに表示される `Graph Editor: http://localhost:11156/` を開くか、MCP から
   `get_available_nodes` 等で疎通確認する
5. 停止は Enter キーまたは Ctrl+C

## 疎通確認チェックリスト

| # | 確認 | 期待 |
|---|---|---|
| H1 | `setup-ngol-embed-sample.ps1` 実行 | `ngol-resources/` に `Nodes/` `WebUI/` `ngol-config.json` が揃う（DLL は入らない） |
| H2 | `dotnet run` | コンソールに `[NgolRuntime] initialized` 等のログ |
| H3 | ブラウザで `http://127.0.0.1:11156` | WebUI画面が表示される |
| H4 | MCP `get_available_nodes` | ノード一覧が返る |

## 既知の制約

- **ホスト固有ノードは含まれない**: `Nodes/Builtin`（BuiltinNodes）は特定のホスト環境に依存しない
  汎用ノードのみを収録している。特定ホストの型に依存するカスタムノードが必要な場合は、そのホスト上で
  `Nodes/CustomNodes/cs/` に自作ノードを配置する
- ホットリロード対象の `Nodes/CustomNodes/cs/` は空で開始する。自作ノードを試す場合はそこに `.cs` を配置する
