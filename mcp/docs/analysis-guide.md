# NGOL 解析出力パターンガイド（AI 向け）

解析グラフを設計する際、状況に応じて Pattern 1〜4 から選択してください。
`MainThreadDispatch` の基本ルール（背景スレッド制約・try-catch が機能しないホストがある等）は
`node-dev-reference.md` を参照。ここでは各パターン固有の差分のみ示す。

## 選択表

| Pattern | 向き | 判断基準 |
|---|---|---|
| **1. LogOutput-JSON** | 小〜中規模（オブジェクト数 < 50） | まずこれを試す |
| **2. ファイル出力** | 大量データ（> 50 件・複雑なフィルタ） | Pattern 1 で JSON が巨大になる／タイムアウトする場合 |
| **3. Snapshot 収集** | 単一〜少数の値 | ノードコーディング不要、グラフだけで完結 |
| **4. RegisterPersistent(OnUpdate)** | `MainThreadDispatch` がタイムアウトする環境 | ホスト側の何らかの理由でメインスレッド処理が停止/低頻度になっている場合（例: Unity ゲームエンジンをホストにした際の `Time.timeScale = 0`） |

---

## Pattern 1: LogOutput-JSON（推奨: 小〜中規模）

解析ノードが `JSON:{...}` 形式でホストのログに出力する。`execute_graph` の Logs から取得する。

```csharp
[NodeType("custom.process_info_scanner", "Custom", "Process Info Scanner")]
public class ProcessInfoScannerNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        var info = $"{{\"name\":\"{proc.ProcessName}\",\"threads\":{proc.Threads.Count}}}";
        ctx.Logger.LogInfo($"JSON:{info}");
    }
}
```

> 数千件を一括ログ出力すると JSON が巨大になりタイムアウトする。多い場合は **Pattern 2**。
> ホストのメインスレッド専用 API を読む場合は `ctx.MainThreadDispatch()` でラップする（`node-dev-reference.md` 参照）。

---

## Pattern 2: ファイル出力 + スクリプト（推奨: 大量データ）

解析ノードが JSON/CSV を `%TEMP%/ngol_output/` に書き出す。AI がスクリプトでフィルタして必要部分だけ取得する。

```csharp
[NodeType("custom.file_list_output", "Custom", "File List Output")]
public class FileListOutputNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var dir = ctx.GetParam<string>("targetDir");
        var data = new System.Collections.Generic.List<object>();
        foreach (var f in System.IO.Directory.EnumerateFiles(dir))
            data.Add(new { name = System.IO.Path.GetFileName(f) });
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ngol_output");
        System.IO.Directory.CreateDirectory(outDir);
        var path = System.IO.Path.Combine(outDir, "files.json");
        System.IO.File.WriteAllText(path, json);
        ctx.Logger.LogInfo($"Written {data.Count} entries to {path}");
    }
}
```

---

## Pattern 3: Snapshot 収集（推奨: 単一〜少数の値）

出力ポートに Snapshot ノードを接続する。`execute_graph` が Snapshots を収集して返す。追加コーディング不要。

```json
{
  "nodes": [
    { "instanceId": "n1", "nodeTypeId": "ngol.logic.add", "paramValues": { "a": 1, "b": 2 } },
    { "instanceId": "n2", "nodeTypeId": "ngol.snapshot.any", "paramValues": { "portName": "value" } }
  ],
  "connections": [
    { "fromNodeInstanceId": "n1", "fromPortName": "result", "toNodeInstanceId": "n2", "toPortName": "value" }
  ]
}
```

---

## Pattern 4: RegisterPersistent(OnUpdate) 経由（メインスレッドが低頻度/停止気味の環境向け）

`MainThreadDispatch` は、ホストのメインスレッドが何らかの理由で長時間ブロックまたは低頻度にしか回っていない環境ではタイムアウトすることがある（例: Unity ゲームエンジンをホストにした際の `Time.timeScale = 0`）。`OnUpdate` はメインスレッドで周期的に呼ばれるコールバックのため、そのような環境でも動作しやすい。

- **判断方法**: まず Ping ノード（MainThreadDispatch なし）で接続確認 → 成功するが解析ノードだけ
  タイムアウトするなら Pattern 4 を使う
- **制約**: `Execute()` は即座に終了するため、結果はファイル出力（Pattern 2）と組み合わせる

```csharp
[NodeType("custom.persistent_safe_scanner", "Custom", "Persistent Safe Scanner")]
public class PersistentSafeScannerNode : INode
{
    public void Execute(IExecutionContext ctx)
    {
        var done = new System.Threading.ManualResetEventSlim(false);
        ctx.RegisterPersistent(new PersistentCallbacks
        {
            OnUpdate = () =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    new { tickCount = System.Environment.TickCount });
                ctx.Logger.LogInfo($"JSON:{json}");
                done.Set();
            }
        });
        done.Wait(System.TimeSpan.FromSeconds(10));
    }
}
```

> `OnUpdate` は永続ノードとして登録されるため、Execute 終了後も実行が続く。1 回だけ取得すれば
> 十分な場合はそのままで問題ない（WebUI または `stop_persistent_node` で停止可能）。

---

## 上記パターンで表現しづらい場合: WebUI プラグイン

Pattern 1〜4 はいずれも「取得した値を AI がそのままテキストとして読む」前提。次のいずれかに
該当する場合は `get_webui_plugin_guide` を検討する:

- 生 JSON/ログの羅列では人間が結果を把握しづらい（階層構造・座標・画像等の可視化が要る）
- 曖昧な候補からの選択・座標指定など、**人間の判断や操作が必要**な解析ステップがある

いずれも NodeGraphModLab 本体は変更せず、`WebUI/plugins/` への `.js` 配置のみで実現する。
