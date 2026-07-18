using System.Text.Json;

namespace NodeGraphModLab.Server.Handlers;

/// <summary>
/// WebUI からノード実行なしでスナップショット値を直接書き込む。
/// UI プラグインで決定した値（選択文字列・座標等）を、対象ノードの断片を実行せずに
/// 断片リンク経由で後続断片へ渡すための機構。
///
/// - value は JSON プリミティブ（string / number / boolean / null）のみ受け付ける
/// - PIN 中は NotifyingSnapshotStore.CanSet ガードにより拒否（success=false, reason="blocked"）
/// - 成功時は snapshot_saved push も送り、WebUI のバッジ表示を即時更新する
///   （クライアント発のスナップショット書き換えは restore_snapshot と同パターン）
/// - ノードの Execute は走らないため、副作用や出力ポート実値は変化しない点に注意
/// </summary>
internal sealed class SetSnapshotValueHandler : IMessageHandler
{
    public string MessageType => "set_snapshot_value";

    public async Task HandleAsync(ISession session, JsonElement root)
    {
        var nodeId = root.TryGetProperty("nodeInstanceId", out var nid) ? nid.GetString() : null;
        var portName = root.TryGetProperty("portName", out var pn) ? pn.GetString() : null;
        if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(portName)) return;
        if (!root.TryGetProperty("value", out var valueEl)) return;

        object? value;
        switch (valueEl.ValueKind)
        {
            case JsonValueKind.String: value = valueEl.GetString(); break;
            case JsonValueKind.Number: value = valueEl.GetDouble(); break;
            case JsonValueKind.True:   value = true;  break;
            case JsonValueKind.False:  value = false; break;
            case JsonValueKind.Null:   value = null;  break;
            default:
                await SendResponseAsync(session, nodeId!, portName!, false, "unsupported_type");
                return;
        }

        var store = session.SnapshotStore;
        // PIN 中は SetSnapshot がサイレント no-op になるため、事前判定して結果を返す
        if (store.CanSet != null && !store.CanSet(nodeId!))
        {
            await SendResponseAsync(session, nodeId!, portName!, false, "blocked");
            return;
        }

        store.SetSnapshot(nodeId!, portName!, value);

        await SendResponseAsync(session, nodeId!, portName!, true, null);

        // バッジ更新用の snapshot_saved push（既存クライアント処理をそのまま流用できる）
        var push = new SnapshotSavedPush
        {
            NodeInstanceId = nodeId!,
            PortName = portName!,
            ValueType = value?.GetType().Name ?? "null",
            ValueString = value?.ToString()
        };
        await session.SendAsync(JsonSerializer.Serialize(push, ServerJsonContext.Default.SnapshotSavedPush));
    }

    private static async Task SendResponseAsync(ISession session, string nodeId, string portName, bool success, string? reason)
    {
        var resp = new SetSnapshotValueResponse
        {
            Success = success,
            NodeInstanceId = nodeId,
            PortName = portName,
            Reason = reason
        };
        await session.SendAsync(JsonSerializer.Serialize(resp, ServerJsonContext.Default.SetSnapshotValueResponse));
    }
}
