using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tokilive;

public static class UpdateService
{
    const string Repo = "ichiyotsu/Tokilive";

    public static async Task Check(bool download)
    {
        var state = AppState.Current;
        state.UpdateMessage = "正在检查…";
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "tokilive/1.3.1 (https://github.com/ichiyotsu/Tokilive)");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            var list = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases?per_page=20"));
            JsonElement? best = null;
            foreach (var rel in list.RootElement.EnumerateArray())
            {
                if (rel.GetProperty("draft").GetBoolean() || rel.GetProperty("prerelease").GetBoolean()) continue;
                var version = rel.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
                if (version.Contains('-') || Compare(version, AppState.Version) <= 0) continue;
                if (best == null || Compare(version, best.Value.GetProperty("tag_name").GetString()!.TrimStart('v', 'V')) > 0) best = rel;
            }
            if (best == null) { state.UpdateMessage = "已经是最新正式版 " + AppState.Version + "。"; return; }
            var release = best.Value;
            var asset = Pick(release.GetProperty("assets"));
            if (asset == null) { state.UpdateMessage = "发现新版本，但没有 Windows 安装包。"; return; }
            var name = asset.Value.GetProperty("name").GetString() ?? "setup.exe";
            var versionName = release.GetProperty("tag_name").GetString();
            if (!download) { state.UpdateMessage = "可以更新到 " + versionName + "（" + name + "）。再点一次开始下载。"; return; }
            var sums = await Sums(http, release.GetProperty("assets"));
            var expected = asset.Value.TryGetProperty("digest", out var digest) ? digest.GetString()?.Replace("sha256:", "") : null;
            if (string.IsNullOrWhiteSpace(expected) && sums.TryGetValue(name, out var fromFile)) expected = fromFile;
            if (string.IsNullOrWhiteSpace(expected) || expected.Length != 64) { state.UpdateMessage = "发行版没有 SHA-256，已拒绝下载。"; return; }
            var id = asset.Value.GetProperty("id").GetInt64();
            var dest = Path.Combine(Path.GetTempPath(), "tokilive-" + name);
            var actual = await Download(http, $"https://api.github.com/repos/{Repo}/releases/assets/{id}", dest);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected.ToLowerInvariant())))
            {
                File.Delete(dest);
                state.UpdateMessage = "校验和不匹配，安装包已删除。";
                return;
            }
            state.UpdateMessage = "已校验 " + name + "。确认后才会打开安装程序。";
            var confirm = await App.Confirm("安装 " + versionName + "？会关闭 tokilive 并打开安装程序。安装程序可以选择目录。");
            if (!confirm) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
            App.UI(() => Microsoft.UI.Xaml.Application.Current.Exit());
        }
        catch (Exception ex) { state.UpdateMessage = ex.Message; }
    }

    static JsonElement? Pick(JsonElement assets)
    {
        JsonElement? setup = null, zip = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase)) setup = asset;
            else if (name.Contains("portable", StringComparison.OrdinalIgnoreCase) && (name.EndsWith(".zip") || name.EndsWith(".exe"))) zip ??= asset;
        }
        return setup ?? zip;
    }

    static async Task<Dictionary<string, string>> Sums(HttpClient http, JsonElement assets)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith("SHA256SUMS", StringComparison.OrdinalIgnoreCase)) continue;
            var text = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/assets/{asset.GetProperty("id").GetInt64()}");
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0].Length == 64) map[parts[1].TrimStart('*').Trim()] = parts[0].ToLowerInvariant();
            }
        }
        return map;
    }

    static async Task<string> Download(HttpClient http, string url, string dest)
    {
        for (var hop = 0; hop < 5; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if ((int)res.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var next = res.Headers.Location?.ToString() ?? "";
                if (!next.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("拒绝非 https 下载地址");
                if (req.Headers.Authorization != null) throw new InvalidOperationException("重定向仍携带授权，已停止。");
                url = next;
                continue;
            }
            if (res.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("下载失败 HTTP " + (int)res.StatusCode);
            await using var input = await res.Content.ReadAsStreamAsync();
            await using var output = File.Create(dest);
            using var hash = SHA256.Create();
            var buf = new byte[64 * 1024];
            int n;
            while ((n = await input.ReadAsync(buf)) > 0) { hash.TransformBlock(buf, 0, n, null, 0); await output.WriteAsync(buf.AsMemory(0, n)); }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
        }
        throw new InvalidOperationException("重定向过多");
    }

    public static int Compare(string a, string b)
    {
        var pa = a.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var pb = b.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var d = (i < pa.Length ? pa[i] : 0) - (i < pb.Length ? pb[i] : 0);
            if (d != 0) return d;
        }
        return 0;
    }
}
