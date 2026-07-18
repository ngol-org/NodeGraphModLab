# NgolPluggableSample

**NGOLを「任意の依存」として組み込む**サンプルです。NodeGraphModLab.Core 等への compile-time
参照を一切持たず（`NgolActivator.cs` 参照）、pluginDir に実体DLLが揃っていれば reflection 経由で
NGOLを起動し、揃っていなければ NGOL 無しの通常アプリとしてビルド・起動とも成立します。

「まずNGOLの素のAPIがどんな形か知りたい」場合は、直接参照版の
[`samples/NgolEmbedSample/`](../NgolEmbedSample/) を先に見ることをおすすめします。
このサンプルは、その組み込みを「NGOLがあれば使う、無ければ使わない」という任意化パターンに
発展させたものです。

## 構成

| ファイル | 役割 |
|---|---|
| `Program.cs` | エントリポイント。pluginDir解決（明示引数→exe近傍自動探索→開発時フォールバック）とコンソール待機ループ |
| `NgolActivator.cs` | NGOL起動処理そのもの。既存プロジェクトへ組み込む場合はこの1ファイルをコピーし `TryStart(pluginDir)` を呼ぶだけでよい |
| `NgolPluggableSample.csproj` | net6.0 コンソールアプリ。NodeGraphModLab.Core等への`<Reference>`は無し（コンパイル時参照ゼロ） |
| `ngol-config.json` | port 11156 既定 |
| `setup-ngol-pluggable-sample.ps1` | リリースzipの`NGOL/`フォルダから`ngol-plugin/`を組み立てる |

## 使い方

1. リリースzipを展開し、`NGOL/` フォルダのパスを確認する
2. `ngol-plugin/` を組み立てる:
   ```powershell
   cd samples\NgolPluggableSample
   .\setup-ngol-pluggable-sample.ps1 -SourceDir "<展開したNGOL/フォルダのパス>"
   ```
3. 起動する:
   ```powershell
   dotnet run -- .\ngol-plugin
   ```
   `ngol-plugin`をexeと同じフォルダかそのサブフォルダに置いた場合は、引数無しでも自動検出される
   （`NodeGraphModLab.Core.dll`の有無で判定）。
4. コンソールに表示される `WebUI: http://127.0.0.1:11156/` を開くか、MCP から
   `get_available_nodes` 等で疎通確認する
5. 停止は Enter キーまたは Ctrl+C（`--autostart` 起動時はCtrl+Cのみ）

`ngol-plugin/`に`NodeGraphModLab.Core.dll`/`NodeAPI.dll`が無い状態で起動すると、NGOLを初期化せず
通常のコンソールアプリとして動作する（ビルドにもDLLは一切不要）。

## 疎通確認チェックリスト

| # | 確認 | 期待 |
|---|---|---|
| H1 | `setup-ngol-pluggable-sample.ps1` 実行 | `ngol-plugin/` に `NodeGraphModLab.Core.dll` 等が揃う |
| H2 | `dotnet run -- .\ngol-plugin` | コンソールに `[NgolRuntime] initialized` 等のログ |
| H3 | ブラウザで `http://127.0.0.1:11156` | WebUI画面が表示される |
| H4 | MCP `get_available_nodes` | ノード一覧が返る |

## 既知の制約

- **ホスト固有ノードは含まれない**: `Nodes/Builtin`（BuiltinNodes）は特定のホスト環境に依存しない
  汎用ノードのみを収録している。特定ホストの型に依存するカスタムノードが必要な場合は、そのホスト上で
  `Nodes/CustomNodes/cs/` に自作ノードを配置する
- ホットリロード対象の`Nodes/CustomNodes/cs/`は空で開始する。自作ノードを試す場合はそこに`.cs`を配置する
- `NodeGraphModLab.HostLogging.dll`はコンソール+ファイル出力の上質なロガーが欲しい場合のオプション
  同梱。無くても`DispatchProxy`による簡易コンソールロガーにフォールバックしてNGOLは起動する
