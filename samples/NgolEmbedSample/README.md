# NgolEmbedSample

追加のランタイムを一切必要とせず、**自作の .NET コンソールアプリから NGOL を
組み込んで起動する**最小サンプルです。展開済みのリリースzip（`NGOL/` フォルダ）から
`ngol-plugin/` を組み立て、そのまま `NgolRuntime` を起動して WebUI・MCP に疎通できます。

NGOLを「あれば使う、無ければ使わない」という任意の依存として組み込みたい場合は、
このサンプルを reflection ベースに発展させた [`samples/NgolPluggableSample/`](../NgolPluggableSample/)
を参照してください。

## 構成

| ファイル | 役割 |
|---|---|
| `Program.cs` | エントリポイント。`ngol-plugin` 引数解決 → `Assembly.LoadFrom` でNGOL本体を動的ロード |
| `NgolActivator.cs` | `NgolRuntime` の起動・停止処理（型解決タイミングの都合で `Program.cs` から分離） |
| `NgolEmbedSample.csproj` | net6.0 コンソールアプリ。Core/NodeAPI/HostLoggingは`Private=false`参照（コンパイル時型解決のみ） |
| `ngol-config.json` | port 11156 既定 |
| `setup-ngol-embed-sample.ps1` | リリースzipの`NGOL/`フォルダから`ngol-plugin/`を組み立てる |

## 使い方

1. リリースzipを展開し、`NGOL/` フォルダのパスを確認する
2. `ngol-plugin/` を組み立てる:
   ```powershell
   cd samples\NgolEmbedSample
   .\setup-ngol-embed-sample.ps1 -SourceDir "<展開したNGOL/フォルダのパス>"
   ```
3. 起動する:
   ```powershell
   dotnet run -- .\ngol-plugin
   ```
4. コンソールに表示される `WebUI: http://127.0.0.1:11156/` を開くか、MCP から
   `get_available_nodes` 等で疎通確認する
5. 停止は Enter キーまたは Ctrl+C（`--autostart` 起動時はCtrl+Cのみ）

## 疎通確認チェックリスト

| # | 確認 | 期待 |
|---|---|---|
| H1 | `setup-ngol-embed-sample.ps1` 実行 | `ngol-plugin/` に `NodeGraphModLab.Core.dll` 等が揃う |
| H2 | `dotnet run -- .\ngol-plugin` | コンソールに `[NgolRuntime] initialized` 等のログ |
| H3 | ブラウザで `http://127.0.0.1:11156` | WebUI画面が表示される |
| H4 | MCP `get_available_nodes` | ノード一覧が返る |

## 既知の制約

- **ホスト固有ノードは含まれない**: `Nodes/Builtin`（BuiltinNodes）は特定のホスト環境に依存しない
  汎用ノードのみを収録している。特定ホストの型に依存するカスタムノードが必要な場合は、そのホスト上で
  `Nodes/CustomNodes/cs/` に自作ノードを配置する
- ホットリロード対象の`Nodes/CustomNodes/cs/`は空で開始する。自作ノードを試す場合はそこに`.cs`を配置する
