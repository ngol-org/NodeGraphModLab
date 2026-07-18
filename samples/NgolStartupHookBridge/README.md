# NgolStartupHookBridge

`DOTNET_STARTUP_HOOKS`（.NET Core 3.0+ の公式機能）を使い、**対象.NETアプリのソースコードを一切変更せずに** NGOL Core を注入起動する技術検証サンプル。

起動スクリプト側で環境変数を設定するだけで、対象アプリの `Main()` 実行前に NGOL のWebSocket/WebUIサーバーが立ち上がる。対象は自作アプリに限らず、`pwsh.exe`（PowerShell 7+）のような既存の .NET アプリにもそのまま注入できる。

## 仕組み

- `StartupHook.cs`: `.NET` ランタイム仕様に従った名前空間なしの `StartupHook.Initialize()`。`DOTNET_STARTUP_HOOKS` 環境変数にこのDLLのパスを指定すると、対象アプリの `Main()` 実行前に自動的に呼ばれる。
- `NgolActivator.cs`: `samples/NgolPluggableSample/NgolActivator.cs` と同じ reflection ベースの起動ロジック（`NodeGraphModLab.Core` 等への compile-time 参照なし）。環境変数 `NGOL_BRIDGE_PLUGIN_DIR`（無ければこのDLLと同じ場所の `ngol-plugin/`）から実体DLLを探して `NgolRuntime` を起動する。
- 例外は `StartupHook.Initialize()` / `NgolActivator.TryStart()` の両方で握りつぶす。`Initialize()` から例外を投げると**ホストプロセスの起動自体が失敗する**ため。

## 使い方: PowerShell (pwsh.exe) への注入

```powershell
# 1. ビルド
dotnet build .\NgolStartupHookBridge.csproj -c Debug

# 2. ngol-plugin/ フォルダを組み立て（Core/NodeAPI/BuiltinNodes/HostLogging + WebUI dist）
.\prepare-ngol-plugin-dir.ps1

# 3. 注入した状態で pwsh.exe を起動
.\launch-pwsh.ps1
```

`pwsh.exe`（NGOLへの参照を一切持たない、普段通りのPowerShellプロセス）が起動し、裏では `ngol-plugin/ngol-config.json` の `port`（既定 `11156`）でWebUI/WebSocketサーバーが動いている。ブラウザで `http://127.0.0.1:11156/` を開くとWebUIにアクセスでき、MCPからも疎通確認できる。

**注意**: `DOTNET_STARTUP_HOOKS` は .NET Core 3.0+ の機能のため、.NET Framework製の旧来の `powershell.exe`（Windows PowerShell 5.1）には効かない。`pwsh.exe`（PowerShell 7+、.NET Core/.NET製）には理論通り注入できることを実機確認している。

- ログ: `startup-hook-bridge.log`（DLL探索など注入前後の診断）, `host.log`（NGOL本体の起動ログ）。いずれもブリッジDLLと同じディレクトリに出力される。

## 既知の制約

- `DOTNET_STARTUP_HOOKS` は**プロセス起動時のみ**有効。既に起動済みのプロセスへの後付け注入には別方式（CLR Profiling API の `ICLRProfiling::AttachProfiler` 等）が必要。
- 自己完結型シングルファイル発行（`PublishSingleFile`）された対象アプリでは動作しない可能性がある（未検証）。
- 対象アプリがすでに同名アセンブリ・同名型を使用している場合は `AssemblyLoadContext.Default` 上で衝突しうる。
