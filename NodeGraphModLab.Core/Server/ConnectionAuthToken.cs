using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NodeGraphModLab.Server;

/// <summary>
/// WebSocket 接続のトークン認証（オプション機能）。
/// 無効時は <see cref="CurrentToken"/> が null のままとなり、<see cref="Validate"/> は常に true を返す（既存動作を維持）。
/// 有効時は起動ごとにトークンファイルを読込・無ければ生成し、以後の WS ハンドシェイクで検証する。
/// </summary>
public static class ConnectionAuthToken
{
    public const string TokenFileName = "ngol-token.txt";

    public static string? CurrentToken { get; private set; }

    /// <summary>
    /// enabled が false の場合は CurrentToken を null にリセットし検証を無効化する。
    /// true の場合はトークンファイルを読込・無ければ新規生成して永続化する
    /// （既存ファイルがあれば再利用するため、無効化→再有効化しても同じトークンが使える）。
    /// </summary>
    public static void Initialize(string pluginDir, bool enabled, int port, INgolLogger log)
    {
        CurrentToken = null;
        if (!enabled) return;

        var path = Path.Combine(pluginDir, TokenFileName);
        string? token = null;
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (!string.IsNullOrEmpty(existing)) token = existing;
        }

        if (token == null)
        {
            token = GenerateToken();
            File.WriteAllText(path, token, Encoding.UTF8);
        }

        CurrentToken = token;
        log.LogInfo($"[Config] Auth token required. Open http://localhost:{port}/?token={token} to connect the WebUI (token file: {path})");
    }

    /// <summary>presented が現在有効なトークンと一致するか検証する。トークン認証が無効（CurrentToken == null）の場合は常に true。</summary>
    public static bool Validate(string? presented)
    {
        return CurrentToken == null || string.Equals(presented, CurrentToken, StringComparison.Ordinal);
    }

    private static string GenerateToken()
    {
        // Base64Url（+/=を含まない）にすることで Sec-WebSocket-Protocol のトークン文字集合にそのまま使える
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
