using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tokilive;

public sealed class ChatLine
{
    public string Type { get; init; } = "danmaku";
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";
}

public static class LiveService
{
    public static ObservableCollection<ChatLine> Lines { get; } = new();
    public static ObservableCollection<ChatLine> Pills { get; } = new();
    public static event Action<string>? Status;
    public static int Likes, Danmaku, Online;
    public static string Watched = "";

    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
    static readonly int[] Mixin = [46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52];
    static CancellationTokenSource? _run;
    static DateTime _nextSend = DateTime.MinValue;
    static string? _mix;

    public static void PushLocal(string type, string name, string text, bool force = false)
    {
        if (!force && !AppState.Current.Allows(type)) return;
        var pills = type is "enter" or "follow" or "like" or "share";
        App.UI(() =>
        {
            var list = pills ? Pills : Lines;
            list.Add(new ChatLine { Type = type, Name = name, Text = text });
            while (list.Count > 200) list.RemoveAt(0);
        });
    }

    public static async Task RefreshAccount()
    {
        if (!AppState.Current.Cookie.Contains("SESSDATA=", StringComparison.Ordinal))
        {
            AppState.Current.Account = "未登录";
            return;
        }
        try
        {
            var nav = await GetJson("https://api.bilibili.com/x/web-interface/nav", AppState.Current.Cookie);
            var data = nav.RootElement.GetProperty("data");
            if (nav.RootElement.GetProperty("code").GetInt32() == 0 && data.GetProperty("isLogin").GetBoolean())
                AppState.Current.Account = data.GetProperty("uname").GetString() ?? "已登录";
            else AppState.Current.Account = "登录已失效";
        }
        catch { AppState.Current.Account = "已保存登录，但没有读到昵称"; }
    }

    public static void Connect(string room)
    {
        Stop();
        _run = new CancellationTokenSource();
        _ = Run(room, _run.Token);
    }

    public static void Stop()
    {
        try { _run?.Cancel(); } catch { }
        _run = null;
    }

    public static async Task<(bool ok, string message)> ReadOwnRoom()
    {
        var cookie = AppState.Current.Cookie;
        if (!cookie.Contains("SESSDATA=", StringComparison.Ordinal))
            return (false, "未登录，无法读取自己的直播间。房间号没有改动。");
        try
        {
            var nav = await GetJson("https://api.bilibili.com/x/web-interface/nav", cookie);
            if (nav.RootElement.GetProperty("code").GetInt32() != 0 || nav.RootElement.GetProperty("data").GetProperty("isLogin").GetBoolean() == false)
                return (false, "登录已失效。房间号没有改动。");
            var info = await GetJson("https://api.live.bilibili.com/xlive/web-ucenter/user/live_info", cookie);
            var data = info.RootElement.GetProperty("data");
            var id = data.TryGetProperty("room_id", out var room) ? room.ToString() : "0";
            if (id is "0" or "") return (false, "这个账号没有直播间。房间号没有改动。");
            return (true, id);
        }
        catch (Exception ex) { return (false, "无法访问直播间接口（" + ex.Message + "）。房间号没有改动。"); }
    }

    public static async Task<(string? pngUrl, string message)> StartQr(Action<string> onCookie)
    {
        var jar = new CookieContainer();
        using var http = new HttpClient(new HttpClientHandler { CookieContainer = jar, UseCookies = true });
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://passport.bilibili.com/x/passport-login/web/qrcode/generate");
        req.Headers.TryAddWithoutValidation("User-Agent", "tokilive/1.3.1");
        req.Headers.Referrer = new Uri("https://www.bilibili.com/");
        var json = JsonDocument.Parse(await (await http.SendAsync(req)).Content.ReadAsStringAsync());
        if (json.RootElement.GetProperty("code").GetInt32() != 0) return (null, "获取二维码失败");
        var data = json.RootElement.GetProperty("data");
        var key = data.GetProperty("qrcode_key").GetString() ?? "";
        var url = data.GetProperty("url").GetString() ?? "";
        _ = PollQr(http, jar, key, onCookie);
        return (url, "请用哔哩哔哩 App 扫码");
    }

    static async Task PollQr(HttpClient http, CookieContainer jar, string key, Action<string> onCookie)
    {
        for (var i = 0; i < 90; i++)
        {
            await Task.Delay(2000);
            var res = await http.GetAsync("https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key=" + Uri.EscapeDataString(key));
            var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var code = json.RootElement.GetProperty("data").GetProperty("code").GetInt32();
            if (code == 86038) { Status?.Invoke("二维码已过期"); return; }
            if (code != 0) continue;
            var cookies = new Dictionary<string, string>();
            foreach (Cookie c in jar.GetAllCookies()) if (!cookies.ContainsKey(c.Name)) cookies[c.Name] = c.Value;
            var jump = json.RootElement.GetProperty("data").GetProperty("url").GetString() ?? "";
            foreach (var keyName in new[] { "SESSDATA", "bili_jct", "DedeUserID" })
                if (!cookies.ContainsKey(keyName))
                {
                    var value = QueryValue(jump, keyName);
                    if (value.Length > 0) cookies[keyName] = value;
                }
            onCookie(string.Join("; ", cookies.Where(p => p.Value.Length > 0).Select(p => p.Key + "=" + p.Value)));
            Status?.Invoke("登录成功");
            return;
        }
    }

    public static async Task<string> Send(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return "没有内容";
        if (text.Length > 40) return "超过 40 字，没有发送";
        var cookie = AppState.Current.Cookie;
        var csrf = CookieValue(cookie, "bili_jct");
        if (!cookie.Contains("SESSDATA=") || csrf.Length == 0) return "未登录，没有发送";
        if (DateTime.UtcNow < _nextSend) return "发送太快，请稍等";
        _nextSend = DateTime.UtcNow.AddMilliseconds(1500);
        if (!int.TryParse(new string(AppState.Current.Room.Where(char.IsDigit).ToArray()), out var room) || room <= 0)
            return "没有房间号，没有发送";
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["bubble"] = "0", ["msg"] = text, ["color"] = "16777215", ["mode"] = "1", ["roomid"] = room.ToString(),
            ["rnd"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ["fontsize"] = "25", ["csrf"] = csrf, ["csrf_token"] = csrf
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.live.bilibili.com/msg/send") { Content = body };
        req.Headers.TryAddWithoutValidation("Cookie", cookie);
        req.Headers.Referrer = new Uri("https://live.bilibili.com/");
        var json = JsonDocument.Parse(await (await Http.SendAsync(req)).Content.ReadAsStringAsync());
        var code = json.RootElement.GetProperty("code").GetInt32();
        return code == 0 ? "已发送" : (json.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "发送失败" : "发送失败");
    }

    static async Task Run(string roomText, CancellationToken token)
    {
        try
        {
            Report("正在解析房间号…");
            var id = new string(roomText.Where(char.IsDigit).ToArray());
            var init = await GetJson("https://api.live.bilibili.com/room/v1/Room/room_init?id=" + id, AppState.Current.Cookie);
            if (init.RootElement.GetProperty("code").GetInt32() != 0) throw new InvalidOperationException("直播间不存在");
            var roomId = init.RootElement.GetProperty("data").GetProperty("room_id").GetInt32();
            var signed = await Wbi(new Dictionary<string, string> { ["id"] = roomId.ToString(), ["type"] = "0", ["web_location"] = "444.8" });
            var info = await GetJson("https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?" + signed, AppState.Current.Cookie);
            var data = info.RootElement.GetProperty("data");
            var host = data.GetProperty("host_list")[0];
            var url = $"wss://{host.GetProperty("host").GetString()}:{host.GetProperty("wss_port").GetInt32()}/sub";
            var auth = JsonSerializer.Serialize(new { uid = 0, roomid = roomId, protover = 2, platform = "web", type = 2, key = data.GetProperty("token").GetString() });
            using var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("User-Agent", "tokilive/1.3.1");
            ws.Options.SetRequestHeader("Origin", "https://live.bilibili.com");
            await ws.ConnectAsync(new Uri(url), token);
            await ws.SendAsync(Pack(7, auth), WebSocketMessageType.Binary, true, token);
            var buf = new byte[1 << 20];
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            var beat = Task.Run(async () => { while (await timer.WaitForNextTickAsync(token)) await ws.SendAsync(Pack(2, ""), WebSocketMessageType.Binary, true, token); }, token);
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buf, token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                Unpack(buf.AsMemory(0, result.Count), roomId);
            }
            await beat;
        }
        catch (OperationCanceledException) { Report("未连接"); }
        catch (Exception ex) { Report("连接失败：" + ex.Message); }
    }

    static void Unpack(ReadOnlyMemory<byte> buf, int roomId)
    {
        var span = buf.Span;
        var off = 0;
        while (off + 16 <= span.Length)
        {
            var len = ReadBE32(span, off);
            var hl = ReadBE16(span, off + 4);
            var ver = ReadBE16(span, off + 6);
            var op = ReadBE32(span, off + 8);
            if (len < 16 || off + len > span.Length) break;
            var body = buf.Slice(off + hl, len - hl);
            off += len;
            if (op == 8) Report("已连接 · 房间 " + roomId);
            else if (op == 5 && ver == 2)
            {
                try { Unpack(Inflate(body), roomId); } catch { }
            }
            else if (op == 5 && ver == 0)
            {
                Handle(Encoding.UTF8.GetString(body.Span));
            }
        }
    }

    static void Handle(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
        cmd = cmd.Split(':')[0];
        if (cmd == "DANMU_MSG")
        {
            var info = root.GetProperty("info");
            var user = info[2];
            var name = user[1].GetString() ?? "观众";
            var text = info[1].GetString() ?? "";
            Danmaku++;
            PushLocal("danmaku", name, text);
            if (AppState.Current.TtsEnabled) SpeechService.Speak(name + "说" + text);
        }
        else if (cmd == "SEND_GIFT" && root.TryGetProperty("data", out var gift))
            PushLocal("gift", gift.GetProperty("uname").GetString() ?? "", (gift.TryGetProperty("giftName", out var gn) ? gn.GetString() : "") + " x" + (gift.TryGetProperty("num", out var n) ? n.GetInt32() : 1));
        else if (cmd == "LIKE_INFO_V3_UPDATE" && root.TryGetProperty("data", out var like))
            Likes = like.TryGetProperty("click_count", out var clicks) ? clicks.GetInt32() : Likes;
        else if ((cmd == "ONLINE_RANK_COUNT" || cmd == "ONLINE_RANK_V2") && root.TryGetProperty("data", out var online))
            Online = online.TryGetProperty("online_count", out var oc) ? oc.GetInt32() : online.TryGetProperty("count", out var cc) ? cc.GetInt32() : Online;
        else if (cmd == "WATCHED_CHANGE" && root.TryGetProperty("data", out var watched))
            Watched = watched.TryGetProperty("text_large", out var t) ? t.GetString() ?? Watched : Watched;
    }

    static async Task<string> Wbi(Dictionary<string, string> query)
    {
        if (_mix == null)
        {
            var nav = await GetJson("https://api.bilibili.com/x/web-interface/nav", AppState.Current.Cookie);
            var img = nav.RootElement.GetProperty("data").GetProperty("wbi_img");
            string Tail(string url) => url.Split('/').Last().Split('.')[0];
            var raw = Tail(img.GetProperty("img_url").GetString() ?? "") + Tail(img.GetProperty("sub_url").GetString() ?? "");
            _mix = new string(Mixin.Select(i => i < raw.Length ? raw[i] : ' ').ToArray()).Trim()[..32];
        }
        query["wts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var q = string.Join("&", query.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(q + _mix))).ToLowerInvariant();
        return q + "&w_rid=" + hash;
    }

    static async Task<JsonDocument> GetJson(string url, string cookie)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "tokilive/1.3.1");
        req.Headers.Referrer = new Uri("https://live.bilibili.com/");
        if (cookie.Length > 0) req.Headers.TryAddWithoutValidation("Cookie", cookie);
        return JsonDocument.Parse(await (await Http.SendAsync(req)).Content.ReadAsStringAsync());
    }

    static byte[] Pack(int op, string body)
    {
        var data = Encoding.UTF8.GetBytes(body);
        var buf = new byte[16 + data.Length];
        WriteBE32(buf, 0, buf.Length);
        WriteBE16(buf, 4, 16);
        WriteBE16(buf, 6, 1);
        WriteBE32(buf, 8, op);
        WriteBE32(buf, 12, 1);
        data.CopyTo(buf, 16);
        return buf;
    }

    static byte[] Inflate(ReadOnlyMemory<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    static int ReadBE32(ReadOnlySpan<byte> b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
    static int ReadBE16(ReadOnlySpan<byte> b, int o) => (b[o] << 8) | b[o + 1];
    static void WriteBE32(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static void WriteBE16(byte[] b, int o, int v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
    static string CookieValue(string cookie, string name)
    {
        foreach (var part in cookie.Split(';'))
        {
            var i = part.IndexOf('=');
            if (i > 0 && part[..i].Trim() == name) return part[(i + 1)..].Trim();
        }
        return "";
    }
    static string QueryValue(string url, string key)
    {
        var q = url.IndexOf('?');
        if (q < 0) return "";
        foreach (var part in url[(q + 1)..].Split('&'))
        {
            var i = part.IndexOf('=');
            if (i > 0 && Uri.UnescapeDataString(part[..i]) == key) return Uri.UnescapeDataString(part[(i + 1)..]);
        }
        return "";
    }
    static void Report(string text) { AppState.Current.Status = text; Status?.Invoke(text); }
}

static class CookieJarExt
{
    public static CookieCollection GetAllCookies(this CookieContainer jar) => jar.GetAllCookiesInternal();
    static CookieCollection GetAllCookiesInternal(this CookieContainer jar)
    {
        var all = new CookieCollection();
        foreach (var domain in new[] { "https://passport.bilibili.com/", "https://www.bilibili.com/", "https://bilibili.com/" })
            foreach (Cookie c in jar.GetCookies(new Uri(domain))) all.Add(c);
        return all;
    }
}
