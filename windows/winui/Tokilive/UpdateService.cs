using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tokilive;

public static class UpdateService
{
    const string Repo = "ichiyotsu/Tokilive";

    // #35(a) 下载进度：收到字节数 / 总字节数（总字节数未知时为 0，
    // 界面应显示为不确定进度）。设置页订阅它就能画进度条。
    // 回调抛异常也不会断下载，见 Download 里的 Report。
    public static event Action<long received, long total>? Progress;

    // #35(b) 最新正式版的发行说明（GitHub release body，截到 2000 字，
    // 和 1.2.0 pickUpdate 一样）。每次 Check 开始时清掉，Check 失败或
    // 没有新版本时保持 null，界面不要拿旧版的说明当新版的显示。
    public static string? LatestNotes { get; private set; }

    public static async Task Check(bool download)
    {
        var state = AppState.Current;
        LatestNotes = null;
        state.UpdateMessage = "正在检查…";
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"tokilive/{AppState.Version} (https://github.com/ichiyotsu/Tokilive)");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var list = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases?per_page=20"));
            if (list.RootElement.ValueKind != JsonValueKind.Array)
            {
                // 限流等错误时 GitHub 返回的是 {"message": ...} 而不是数组，
                // 以前 EnumerateArray() 直接抛异常，用户只看到一句英文报错。
                state.UpdateMessage = "检查失败：GitHub 返回异常（可能触发限流），请稍后再试。";
                return;
            }
            JsonElement? best = null;
            string bestVersion = "";
            foreach (var rel in list.RootElement.EnumerateArray())
            {
                // 缺字段或字段类型不对的发行版直接跳过，不能让它抛异常把整个检查拖垮。
                if (!TryFlag(rel, "draft", out var isDraft) || !TryFlag(rel, "prerelease", out var isPre)) continue;
                if (isDraft || isPre) continue;
                if (!rel.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) continue;
                var version = tag.GetString()?.Trim().TrimStart('v', 'V') ?? "";
                if (version.Contains('-') || Compare(version, AppState.Version) <= 0) continue;
                if (best == null || Compare(version, bestVersion) > 0) { best = rel; bestVersion = version; }
            }
            if (best == null) { state.UpdateMessage = "已经是最新正式版 " + AppState.Version + "。"; return; }
            var release = best.Value;
            LatestNotes = release.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() : null;
            if (LatestNotes?.Length > 2000)
            {
                LatestNotes = LatestNotes[..2000];
                // 别在代理对中间截断，否则新版说明里会多一个孤立代理项（显示为乱码）。
                if (char.IsHighSurrogate(LatestNotes[^1])) LatestNotes = LatestNotes[..^1];
            }
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) { state.UpdateMessage = "发现新版本，但没有 Windows 安装包。"; return; }
            var asset = Pick(assets);
            if (asset == null) { state.UpdateMessage = "发现新版本，但没有 Windows 安装包。"; return; }
            var name = AssetName(asset.Value) ?? "setup.exe";
            var versionName = release.TryGetProperty("tag_name", out var tagName) && tagName.ValueKind == JsonValueKind.String ? tagName.GetString() : bestVersion;
            if (!download) { state.UpdateMessage = "可以更新到 " + versionName + "（" + name + "）。再点一次开始下载。"; return; }
            var sums = await Sums(http, assets);
            var expected = asset.Value.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String ? digest.GetString() : null;
            if (expected != null && expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) expected = expected[7..];
            if (string.IsNullOrWhiteSpace(expected) && sums.TryGetValue(name, out var fromFile)) expected = fromFile;
            // 只看长度不够：64 位非 hex 也会在 FromHexString 抛异常，变成一句英文报错。
            if (string.IsNullOrWhiteSpace(expected) || !IsHex64(expected.Trim())) { state.UpdateMessage = "发行版没有 SHA-256，已拒绝下载。"; return; }
            expected = expected.Trim().ToLowerInvariant();
            if (!asset.Value.TryGetProperty("id", out var assetId) || assetId.ValueKind != JsonValueKind.Number || !assetId.TryGetInt64(out var id)) { state.UpdateMessage = "发现新版本，但没有 Windows 安装包。"; return; }
            var dest = Path.Combine(Path.GetTempPath(), "tokilive-" + name);
            var actual = await Download(http, $"https://api.github.com/repos/{Repo}/releases/assets/{id}", dest);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected)))
            {
                File.Delete(dest);
                state.UpdateMessage = "校验和不匹配，安装包已删除。";
                return;
            }
            // #35(c) 便携版不再“打开 zip 当更新”：没有安装包、只有便携版时，
            // 用 1.2.0 的 PowerShell 自替换脚本（等进程退出再替换，绝不在
            // 运行时删/盖正在跑的 exe），替换完重新打开。脚本写不出来或
            // 拿不到当前程序路径时，只告诉用户文件下载到了哪里，不启动它。
            var isPortable = IsPortable(asset.Value);
            var isZip = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            state.UpdateMessage = isPortable
                ? "已校验 " + name + "（便携版）。确认后会关闭 tokilive 并替换当前程序。"
                : "已校验 " + name + "。确认后才会打开安装程序。";
            var confirm = await App.Confirm(isPortable
                ? isZip
                    ? "安装 " + versionName + "（便携版）？会关闭 tokilive，把已校验的压缩包解压覆盖到当前程序目录，再重新打开。"
                    : "安装 " + versionName + "（便携版）？会关闭 tokilive，用已校验的文件替换当前程序，再重新打开。"
                : "安装 " + versionName + "？会关闭 tokilive 并打开安装程序。安装程序可以选择目录。");
            if (!confirm) return;
            if (isPortable)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    state.UpdateMessage = "便携版已下载到 " + dest + "，拿不到当前程序路径，无法自动替换。请关闭 tokilive 后手动解压覆盖，再重新打开。";
                    return;
                }
                string script;
                try { script = WritePortableScript(isZip, dest, exe); }
                catch (Exception ex)
                {
                    state.UpdateMessage = "便携版已下载到 " + dest + "，自动替换脚本写不出来（" + ex.Message + "），请关闭 tokilive 后手动替换。";
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true });
                App.UI(() => Microsoft.UI.Xaml.Application.Current.Exit());
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
            App.UI(() => Microsoft.UI.Xaml.Application.Current.Exit());
        }
        catch (Exception ex) { state.UpdateMessage = ex.Message; }
    }

    // 缺字段或类型不对的 asset 直接跳过，不抛异常。
    static string? AssetName(JsonElement asset)
        => asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;

    // 读 draft / prerelease 这类布尔字段：缺字段或不是布尔值时返回 false，
    // 调用方把该发行版整个跳过。
    static bool TryFlag(JsonElement el, string name, out bool value)
    {
        value = false;
        if (!el.TryGetProperty(name, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.True) { value = true; return true; }
        return prop.ValueKind == JsonValueKind.False;
    }

    static bool IsHex64(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

    // 优先顺序：安装器 > MSIX > 其他 exe 安装器 > 单文件便携版 exe
    // > 便携版 zip。1.2.0 的 pickAsset 也是按类型挑，不是谁先出现挑谁；
    // 以前 setup ?? zip 会把“-setup.exe”和便携版 zip 之外的 msix、
    // 普通 exe 安装器都漏掉，而且 zip 一被挑中就会被当安装程序打开。
    static JsonElement? Pick(JsonElement assets)
    {
        JsonElement? setup = null, msix = null, installer = null, portableExe = null, zip = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = AssetName(asset) ?? "";
            if (name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase)) setup ??= asset;
            else if (name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase)) msix ??= asset;
            else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("portable", StringComparison.OrdinalIgnoreCase)) portableExe ??= asset;
                else installer ??= asset;
            }
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip ??= asset;
        }
        return setup ?? msix ?? installer ?? portableExe ?? zip;
    }

    // 任何 zip、或名字带 portable 的，都是便携版：没有安装程序可启动，
    // 必须走自替换脚本，不能 UseShellExecute 打开。
    static bool IsPortable(JsonElement asset)
    {
        var name = AssetName(asset) ?? "";
        return name.Contains("portable", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    // 1.2.0 update-core.js portableScript 的 WinUI 版：等当前进程退出
    // 再替换，绝不在运行时删/盖正在跑的 exe。zip 多一步：先解到临时
    // 目录再整批拷过去，解压失败时不动程序目录；包只有一层文件夹时
    // （根目录只有一个文件夹）取其内容，避免在程序目录里多套一层。
    // 替换失败时错误写进 %TEMP%\tokilive-update-error.log，程序保持关闭，
    // 不会悄悄留在半替换状态还假装成功。
    static string WritePortableScript(bool isZip, string source, string exe)
    {
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        var dir = Path.GetDirectoryName(exe);
        if (pid <= 0 || string.IsNullOrWhiteSpace(dir)) throw new InvalidOperationException("当前进程信息不完整");
        foreach (var p in new[] { source, exe, dir })
            if (string.IsNullOrWhiteSpace(p) || p.Contains('\r') || p.Contains('\n'))
                throw new InvalidOperationException("路径不合法");
        string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var lines = new List<string>
        {
            "$ErrorActionPreference = 'Stop'",
            "$log = Join-Path $env:TEMP 'tokilive-update-error.log'",
            "try {",
            $"  Wait-Process -Id {pid} -ErrorAction SilentlyContinue",
            "  Start-Sleep -Milliseconds 800",
        };
        if (isZip)
        {
            lines.Add("  $stage = Join-Path $env:TEMP ('tokilive-update-' + [Guid]::NewGuid().ToString('N'))");
            lines.Add("  New-Item -ItemType Directory -Path $stage -Force | Out-Null");
            lines.Add($"  Expand-Archive -LiteralPath {Q(source)} -DestinationPath $stage -Force");
            lines.Add("  $items = Get-ChildItem -LiteralPath $stage");
            lines.Add("  if ($items.Count -eq 1 -and $items[0].PSIsContainer) { $items = Get-ChildItem -LiteralPath $items[0].FullName }");
            lines.Add($"  $items | Copy-Item -Destination {Q(dir)} -Recurse -Force");
            lines.Add("  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue");
            lines.Add($"  Remove-Item -LiteralPath {Q(source)} -Force -ErrorAction SilentlyContinue");
        }
        else
        {
            lines.Add($"  Copy-Item -LiteralPath {Q(source)} -Destination {Q(exe)} -Force");
            lines.Add($"  Remove-Item -LiteralPath {Q(source)} -Force -ErrorAction SilentlyContinue");
        }
        lines.Add($"  Start-Process -FilePath {Q(exe)}");
        lines.Add("} catch {");
        lines.Add("  try { Set-Content -LiteralPath $log -Value $_.Exception.Message } catch { }");
        lines.Add("}");
        lines.Add("Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue");
        var script = string.Join("\n", lines);
        // 1.2.0 同款护栏：脚本里不许出现通配符和 taskkill。
        if (script.Contains('*') || script.Contains("taskkill", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("替换脚本校验失败");
        var path = Path.Combine(Path.GetTempPath(), "tokilive-update-" + Guid.NewGuid().ToString("N")[..8] + ".ps1");
        File.WriteAllText(path, script);
        return path;
    }

    static async Task<Dictionary<string, string>> Sums(HttpClient http, JsonElement assets)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets.EnumerateArray())
        {
            var name = AssetName(asset) ?? "";
            if (!name.StartsWith("SHA256SUMS", StringComparison.OrdinalIgnoreCase)) continue;
            if (!asset.TryGetProperty("id", out var assetId) || assetId.ValueKind != JsonValueKind.Number || !assetId.TryGetInt64(out var id)) continue;
            // GitHub 的 asset API 默认返回 JSON 元数据（Accept 是 vnd.github+json），
            // 必须显式要 application/octet-stream 才会返回文件内容。
            // 以前直接 GetStringAsync 拿到的是 JSON，解析不出任何一行，sums 表
            // 一直是空的，只能靠 asset 自带的 digest；没有 digest 的发行版
            // 就会被误判“没有 SHA-256，已拒绝下载”。
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/assets/{id}");
            req.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
            using var res = await http.SendAsync(req);
            if (res.StatusCode != HttpStatusCode.OK) continue;
            var text = await res.Content.ReadAsStringAsync();
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && IsHex64(parts[0])) map[parts[1].TrimStart('*').Trim()] = parts[0].ToLowerInvariant();
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
            var total = res.Content.Headers.ContentLength ?? 0;
            void Report(long received)
            {
                try { Progress?.Invoke(received, total); }
                catch { /* 进度回调炸了也不能断下载、坏校验 */ }
            }
            Report(0);
            await using var input = await res.Content.ReadAsStreamAsync();
            await using var output = File.Create(dest);
            using var hash = SHA256.Create();
            var buf = new byte[64 * 1024];
            long received = 0;
            int n;
            while ((n = await input.ReadAsync(buf)) > 0)
            {
                hash.TransformBlock(buf, 0, n, null, 0);
                await output.WriteAsync(buf.AsMemory(0, n));
                received += n;
                Report(received);
            }
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
