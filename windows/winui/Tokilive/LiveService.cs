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
    // 1.2.0 里弹幕默认 30 秒后淡出、实时窗互动默认 6 秒后淡出。
    // 1.3.1 把时间戳丢了，消息会一直留在列表里（上限 200 条），
    // 悬浮窗只取最后几条，看起来像卡住不动。
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    // 1.2.0 overlay.js render() / bili.js handle() 里的卡片字段：
    // 头像只用消息里自带的 face（不另外调接口查头像，免得每条弹幕都打一次 API），
    // 粉丝牌来自 medal / medal_info，舰长等级来自 guard / guard_level，
    // 表情来自 DANMU_MSG 的 i[0][13].url，醒目留言的价格和底色来自
    // SUPER_CHAT_MESSAGE 的 price / background_bottom_color。
    // Price 的单位跟 1.2.0 一样按类型走：醒目留言是元，礼物是金瓜子
    //（total_coin），只用来显示醒目留言价格和算礼物最低价。
    public string Face { get; init; } = "";
    public string MedalName { get; init; } = "";
    public int MedalLevel { get; init; }
    public string MedalColor { get; init; } = "";
    public bool Admin { get; init; }
    public int GuardLevel { get; init; }
    public string Emote { get; init; } = "";
    public int Price { get; init; }
    public string Color { get; init; } = "";
    // #20：SUPER_CHAT_MESSAGE 的 time（持续秒数，1.2.0 duration: d.time）。
    // 0 表示用默认 DanmakuLifetime；>0 时实际停留取 max(默认, duration)，见 LifetimeOf()。
    public double DurationSeconds { get; init; }
}

public static class LiveService
{
    public static ObservableCollection<ChatLine> Lines { get; } = new();
    public static ObservableCollection<ChatLine> Pills { get; } = new();
    public static event Action<string>? Status;
    // 1.3.1 的 bug：点赞 / 在线 / 看过人数变了只改了这几个 int，
    // 没有任何事件通知界面，MainWindow 只订阅了 Lines/Pills 的
    // CollectionChanged，所以实况窗数字要等下一条弹幕来了才刷新。
    public static event Action? StatsChanged;
    public static int Likes, Danmaku, Online;
    public static string Watched = "";

    // 对齐 1.2.0 的默认值：弹幕 lifetime 30 秒，实时窗互动 miniLifetime 6 秒。
    // 这两个数现在直接读 AppState（设置页可改）：DanmakuLifetime 0 = 不消失，
    // MiniLifetime 0 = 不消失。以前这里是写死的 readonly 常量，设置改了也不生效。
    public static TimeSpan PillLifetime => AppState.Current.MiniLifetime <= 0 ? TimeSpan.MaxValue : TimeSpan.FromSeconds(AppState.Current.MiniLifetime);

    static bool IsExpired(ChatLine line, double lifetimeSeconds) =>
        // 不直接 TimeSpan.FromSeconds：PositiveInfinity / TimeSpan.MaxValue
        // 的秒数会让 FromSeconds 抛 OverflowException，这里只比秒数。
        !double.IsPositiveInfinity(lifetimeSeconds) && (DateTimeOffset.UtcNow - line.At).TotalSeconds > lifetimeSeconds;

    // #20：一条弹幕的实际停留秒数。醒目留言按 duration 延长，
    // 取 max(默认, duration)；0 表示用默认 lifetime。
    // DanmakuLifetime 设成 0 = 永不消失，返回 PositiveInfinity，
    // IsExpired / OverlayWindow 的 Render 都把它当“不过期”。
    public static double LifetimeOf(ChatLine line)
    {
        var baseLifetime = AppState.Current.DanmakuLifetime;
        if (baseLifetime <= 0) return double.PositiveInfinity;
        return line.DurationSeconds > 0
            ? Math.Max(baseLifetime, line.DurationSeconds)
            : baseLifetime;
    }

    public static bool HasExpired() =>
        Lines.TakeLast(Math.Max(1, AppState.Current.MaxMessages)).Any(l => IsExpired(l, LifetimeOf(l))) ||
        Pills.TakeLast(Math.Max(1, AppState.Current.MiniMax)).Any(l => IsExpired(l, PillLifetime.TotalSeconds));

    static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
    static readonly int[] Mixin = [46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52];
    static CancellationTokenSource? _run;
    static DateTime _nextSend = DateTime.MinValue;
    static string? _mix;
    // #17：buvid3 只在会话内缓存，不写盘（null = 还没拿到或上次获取失败，下次重连再试）。
    static string? _buvid;
    // #18：看门狗用的最后收到数据时间，每收到一帧 WebSocket 数据就更新。
    static DateTime _lastRx = DateTime.UtcNow;
    // 1.2.0 bili.js resolveRoom() 读 room_init 的 data.live_status（1=开播），
    // 连接成功时据此报“未开播”。RunOnce 里记下来，Unpack 收到 op==8 时用。
    static int _liveStatus = 1;

    public static void PushLocal(string type, string name, string text, bool force = false, string emote = "") =>
        PushLine(new ChatLine { Type = type, Name = name, Text = text, Emote = emote }, force);

    public static void PushLine(ChatLine line, bool force = false)
    {
        if (!force && !AppState.Current.Allows(line.Type)) return;
        var isPill = line.Type is "enter" or "follow" or "like" or "share";
        App.UI(() =>
        {
            ObservableCollection<ChatLine> list;
            if (isPill)
            {
                // 1.2.0 的 route：mini = 实时窗，main = 弹幕窗，off = 丢掉。
                // 默认全是 mini，和以前一样进 Pills。
                var route = RouteFor(line.Type);
                if (route == "off") return;
                list = route == "main" ? Lines : Pills;
            }
            else list = Lines;
            list.Add(line);
            // 上限也读设置：弹幕窗 MaxMessages（默认 30），实时窗 MiniMax（默认 5）。
            var cap = ReferenceEquals(list, Lines) ? Math.Max(1, AppState.Current.MaxMessages) : Math.Max(1, AppState.Current.MiniMax);
            while (list.Count > cap) list.RemoveAt(0);
        });
    }

    static string RouteFor(string type) => type switch
    {
        "enter" => AppState.Current.RouteEnter,
        "follow" => AppState.Current.RouteFollow,
        "share" => AppState.Current.RouteShare,
        "like" => AppState.Current.RouteLike,
        _ => "mini"
    };

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
        req.Headers.TryAddWithoutValidation("User-Agent", $"tokilive/{AppState.Version}");
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

    // 1.2.0 断线后指数退避重连（2 秒起，最长 60 秒）；1.3.1 断一次就停了。
    static async Task Run(string roomText, CancellationToken token)
    {
        var retry = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await RunOnce(roomText, token);
                retry = 0;
                // RunOnce 正常返回说明服务器关了连接，不是用户主动断开就重连。
                if (token.IsCancellationRequested) return;
            }
            catch (OperationCanceledException) { Report("未连接"); return; }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) { Report("未连接"); return; }
                // 指数退避：2、4、8、16、32、60、60……秒。
                var wait = Math.Min(60, (int)Math.Pow(2, Math.Min(++retry, 6)));
                Report($"连接失败：{ex.Message}，{wait} 秒后重连…");
                try { await Task.Delay(TimeSpan.FromSeconds(wait), token); }
                catch (OperationCanceledException) { Report("未连接"); return; }
            }
        }
    }

    static async Task RunOnce(string roomText, CancellationToken token)
    {
            Report("正在解析房间号…");
            // #19：每次（重）连都把统计清零（1.2.0 main.js connect() 里 stats = {}），
            // 并通知界面清空数字。
            Likes = 0; Danmaku = 0; Online = 0; Watched = "";
            StatsChanged?.Invoke();
            var id = new string(roomText.Where(char.IsDigit).ToArray());
            var init = await GetJson("https://api.live.bilibili.com/room/v1/Room/room_init?id=" + id, AppState.Current.Cookie);
            if (init.RootElement.GetProperty("code").GetInt32() != 0) throw new InvalidOperationException("直播间不存在");
            var initData = init.RootElement.GetProperty("data");
            _liveStatus = initData.TryGetProperty("live_status", out var ls) && ls.ValueKind == JsonValueKind.Number ? ls.GetInt32() : 1;
            var roomId = initData.GetProperty("room_id").GetInt32();
            var signed = await Wbi(new Dictionary<string, string> { ["id"] = roomId.ToString(), ["type"] = "0", ["web_location"] = "444.8" });
            var info = await GetJson("https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?" + signed, AppState.Current.Cookie);
            var data = info.RootElement.GetProperty("data");
            var host = data.GetProperty("host_list")[0];
            var url = $"wss://{host.GetProperty("host").GetString()}:{host.GetProperty("wss_port").GetInt32()}/sub";
            // #16：cookie 里的 DedeUserID 就是登录 uid（1.2.0 bili.js start()），
            // 没有或解析失败才用 0 匿名；匿名连接用户名会被 B 站打码。
            var uid = long.TryParse(CookieValue(AppState.Current.Cookie, "DedeUserID"), out var parsedUid) ? parsedUid : 0;
            // #17：cookie 里没有 buvid3 就向 finger/spi 要一个，失败也不阻塞连接。
            var buvid = await EnsureBuvidAsync();
            var authObj = new Dictionary<string, object>
            {
                ["uid"] = uid, ["roomid"] = roomId, ["protover"] = 2, ["platform"] = "web", ["type"] = 2,
                ["key"] = data.GetProperty("token").GetString() ?? ""
            };
            if (buvid.Length > 0) authObj["buvid"] = buvid;
            var auth = JsonSerializer.Serialize(authObj);
            using var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("User-Agent", $"tokilive/{AppState.Version}");
            ws.Options.SetRequestHeader("Origin", "https://live.bilibili.com");
            await ws.ConnectAsync(new Uri(url), token);
            await ws.SendAsync(Pack(7, auth), WebSocketMessageType.Binary, true, token);
            _lastRx = DateTime.UtcNow;
            var buf = new byte[1 << 20];
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            var beat = Task.Run(async () =>
            {
                try { while (await timer.WaitForNextTickAsync(token)) await ws.SendAsync(Pack(2, ""), WebSocketMessageType.Binary, true, token); }
                catch { /* 连接断开/停止时心跳退出 */ }
            }, token);
            // #18：75 秒无数据看门狗（1.2.0 bili.js：每 10 秒检查一次，超 75 秒 terminate）。
            // Abort() 让 ReceiveAsync 抛错，走 Run() 已有的指数退避重连，不另写重连机制。
            var watchdog = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), token);
                        if (DateTime.UtcNow - _lastRx > TimeSpan.FromSeconds(75))
                        {
                            Report("75 秒没有收到数据，正在重连…");
                            ws.Abort();
                            break;
                        }
                    }
                }
                catch { /* 停止时退出 */ }
            }, token);
            while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                // 1.3.1 只读一帧就解析，分片的大帧会被当成不完整包丢掉。
                // 读到 EndOfMessage 为止再解析。
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buf, token);
                    if (result.Count > 0) _lastRx = DateTime.UtcNow;
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buf, 0, result.Count);
                } while (!result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Close) break;
                Unpack(ms.ToArray(), roomId);
            }
            await beat;
            await watchdog;
    }

    // #17：cookie 里没有 buvid3 时向 finger/spi 要一个（1.2.0 ensureBuvid）。
    // 失败就返回 ""、连接继续不带 buvid；只在会话内缓存，不写盘。
    static async Task<string> EnsureBuvidAsync()
    {
        if (_buvid != null) return _buvid;
        var fromCookie = CookieValue(AppState.Current.Cookie, "buvid3");
        if (fromCookie.Length > 0) { _buvid = fromCookie; return _buvid; }
        try
        {
            var spi = await GetJson("https://api.bilibili.com/x/frontend/finger/spi", AppState.Current.Cookie);
            var b = spi.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("b_3", out var b3) && b3.ValueKind == JsonValueKind.String
                ? b3.GetString() ?? "" : "";
            if (b.Length > 0) _buvid = b;
            return b;
        }
        catch { return ""; }
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
            if (op == 8) Report("已连接 · 房间 " + roomId + (_liveStatus == 1 ? "" : "（未开播，开播后自动显示弹幕）"));
            else if (op == 5 && ver == 2)
            {
                try { Unpack(Inflate(body), roomId); } catch { }
            }
            else if (op == 5 && ver == 3)
            {
                // 1.2.0 用 protover 3，服务器会发 brotli 压缩包；1.3.1 只处理了
                // zlib（ver 2），ver 3 的包会被直接丢掉。BrotliStream 在 .NET 里自带。
                try
                {
                    using var input = new MemoryStream(body.ToArray());
                    using var brotli = new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    brotli.CopyTo(output);
                    Unpack(output.ToArray(), roomId);
                }
                catch { }
            }
            else if (op == 5 && ver == 0)
            {
                Handle(Encoding.UTF8.GetString(body.Span));
            }
        }
    }

    static void Handle(string json)
    {
        // 1.3.1 的 bug：这里用无保护的 GetProperty / 索引访问，一条坏消息
        // 抛异常就会一路冒泡到 Run() 的 catch，整个连接断掉还不重连。
        // 1.2.0 是逐条 .catch(()=>{}) 兜底 + 指数退避重连。
        try { HandleCore(json); }
        catch { /* 单条消息坏了只丢这一条 */ }
    }

    // 下面几个小帮手都是为了按 1.2.0 bili.js 的结构安全地读 B 站包：
    // DANMU_MSG 的 info 是数组，越界直接索引会抛异常（外层 Handle 会吞掉整条）。
    static JsonElement? ArrAt(JsonElement el, int i) =>
        el.ValueKind == JsonValueKind.Array && i >= 0 && i < el.GetArrayLength() ? el[i] : null;
    static string GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static int GetInt(JsonElement el, string name, int fallback = 0) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;
    static string HexColor(int n) => "#" + (n & 0xffffff).ToString("X6");
    // 粉丝牌：medal_info / fans_medal 都是 { medal_name, medal_level, medal_color(整数) }。
    static (string name, int level, string color) MedalFrom(JsonElement el, string container)
    {
        if (!el.TryGetProperty(container, out var m) || m.ValueKind != JsonValueKind.Object) return ("", 0, "");
        var name = GetStr(m, "medal_name");
        var level = GetInt(m, "medal_level");
        if (name.Length == 0 || level == 0) return ("", 0, "");
        var color = m.TryGetProperty("medal_color", out var c) && c.ValueKind == JsonValueKind.Number ? HexColor(c.GetInt32()) : "";
        return (name, level, color);
    }

    static void HandleCore(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
        cmd = cmd.Split(':')[0];
        if (cmd == "DANMU_MSG")
        {
            // 1.2.0 bili.js：i[1]=文本，i[2]=用户[uid,名字,是否房管,…]，
            // i[3]=粉丝牌[等级,名字,主播,房间,颜色]，i[7]=舰长等级，
            // i[0][13].url=表情图，i[0][15].user.base.face=头像。
            // 头像只用包里自带的 face，不另外调接口查（每条弹幕查一次会打爆 API）。
            var info = root.GetProperty("info");
            var user = ArrAt(info, 2);
            var name = user is { } u2 && u2.ValueKind == JsonValueKind.Array && u2.GetArrayLength() > 1
                ? u2[1].GetString() ?? "观众" : "观众";
            var text = ArrAt(info, 1) is { } t && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            string face = "", emote = "", medalName = "", medalColor = "";
            int medalLevel = 0, guard = 0;
            var admin = false;
            if (ArrAt(info, 0) is { } i0 && i0.ValueKind == JsonValueKind.Array)
            {
                if (ArrAt(i0, 13) is { } emo && emo.ValueKind == JsonValueKind.Object)
                    emote = GetStr(emo, "url");
                if (ArrAt(i0, 15) is { } ext && ext.ValueKind == JsonValueKind.Object
                    && ext.TryGetProperty("user", out var eu) && eu.ValueKind == JsonValueKind.Object
                    && eu.TryGetProperty("base", out var eb) && eb.ValueKind == JsonValueKind.Object)
                    face = GetStr(eb, "face");
            }
            if (user is { } u && u.ValueKind == JsonValueKind.Array && u.GetArrayLength() > 2
                && u[2].ValueKind == JsonValueKind.True) admin = true;
            if (ArrAt(info, 3) is { } med && med.ValueKind == JsonValueKind.Array && med.GetArrayLength() > 1
                && med[1].ValueKind == JsonValueKind.String && (med[1].GetString() ?? "").Length > 0)
            {
                medalLevel = med[0].ValueKind == JsonValueKind.Number ? med[0].GetInt32() : 0;
                medalName = med[1].GetString() ?? "";
                if (med.GetArrayLength() > 4 && med[4].ValueKind == JsonValueKind.Number)
                    medalColor = HexColor(med[4].GetInt32());
            }
            if (ArrAt(info, 7) is { } g && g.ValueKind == JsonValueKind.Number) guard = g.GetInt32();
            Danmaku++;
            PushLine(new ChatLine { Type = "danmaku", Name = name, Text = text, Face = face, Emote = emote, MedalName = medalName, MedalLevel = medalLevel, MedalColor = medalColor, Admin = admin, GuardLevel = guard });
            if (AppState.Current.TtsEnabled) SpeechService.Speak(name + "说" + text);
        }
        else if (cmd == "SEND_GIFT" && root.TryGetProperty("data", out var gift))
        {
            var gName = GetStr(gift, "uname");
            var num = GetInt(gift, "num", 1);
            var gGift = GetStr(gift, "giftName") + " x" + num;
            // 1.2.0 overlay.js：minGiftPrice>0 时，非金瓜子礼物直接不显示，
            // 金瓜子礼物按 price/1000（元）和最低价比。price 在这里是 total_coin。
            var coin = GetStr(gift, "coin_type");
            var price = gift.TryGetProperty("total_coin", out var tc) && tc.ValueKind == JsonValueKind.Number
                ? tc.GetInt32() : GetInt(gift, "price") * num;
            if (AppState.Current.MinGiftPrice > 0)
            {
                if (coin != "gold") return;
                if (price / 1000.0 < AppState.Current.MinGiftPrice) return;
            }
            var (mName, mLevel, mColor) = MedalFrom(gift, "medal_info");
            PushLine(new ChatLine { Type = "gift", Name = gName, Text = gGift, Face = GetStr(gift, "face"), MedalName = mName, MedalLevel = mLevel, MedalColor = mColor, Price = price });
            // 1.2.0 播报覆盖弹幕/礼物/醒目留言/上舰，1.3.1 只播弹幕。
            if (AppState.Current.TtsEnabled) SpeechService.Speak($"感谢{gName}投喂的{gGift}");
        }
        else if (cmd == "SUPER_CHAT_MESSAGE" && root.TryGetProperty("data", out var sc))
        {
            var price = GetInt(sc, "price");
            var uname = sc.TryGetProperty("user_info", out var u) ? GetStr(u, "uname") : "";
            var text = GetStr(sc, "message");
            // 1.2.0：color 取 background_bottom_color，没有再取 background_color。
            var color = GetStr(sc, "background_bottom_color");
            if (color.Length == 0) color = GetStr(sc, "background_color");
            // #20：SUPER_CHAT_MESSAGE 的 time 是持续秒数（1.2.0 duration: d.time），
            // 实际停留取 max(默认 30 秒, duration)，见 LifetimeOf()。
            var duration = sc.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetDouble(out var d) ? d : 0;
            var (mName, mLevel, mColor) = MedalFrom(sc, "medal_info");
            PushLine(new ChatLine { Type = "superchat", Name = uname, Text = text, Face = sc.TryGetProperty("user_info", out var uu) ? GetStr(uu, "face") : "", MedalName = mName, MedalLevel = mLevel, MedalColor = mColor, Price = price, Color = color, DurationSeconds = duration });
            if (AppState.Current.TtsEnabled) SpeechService.Speak($"{uname}的醒目留言：{text}");
        }
        else if (cmd == "GUARD_BUY" && root.TryGetProperty("data", out var guard))
        {
            var level = GetInt(guard, "guard_level");
            var giftName = GetStr(guard, "gift_name");
            if (giftName.Length == 0) giftName = level switch { 1 => "总督", 2 => "提督", 3 => "舰长", _ => "上舰" };
            var num = GetInt(guard, "num", 1);
            var guardName = GetStr(guard, "username");
            // 1.2.0：price/1000（元）。face 用包里自带的，不另外查。
            PushLine(new ChatLine { Type = "guard", Name = guardName, Text = $"{giftName} x{num}", Face = GetStr(guard, "face"), GuardLevel = level, Price = GetInt(guard, "price") / 1000 });
            if (AppState.Current.TtsEnabled) SpeechService.Speak($"感谢{guardName}开通{giftName}");
        }
        else if (cmd == "INTERACT_WORD" && root.TryGetProperty("data", out var iw))
            PushInteract(iw);
        else if (cmd == "INTERACT_WORD_V2" && root.TryGetProperty("data", out var iw2) &&
                 iw2.TryGetProperty("pb", out var pb) && pb.GetString() is string b64 && b64.Length > 0)
        {
            // 1.2.0 的 decodeInteract：protobuf 字段 1=uid、2=uname、5=msg_type。
            try { PushInteract(DecodeInteract(Convert.FromBase64String(b64))); }
            catch { /* V2 解不出来就丢掉这一条，不让整个连接崩掉 */ }
        }
        else if (cmd == "LIKE_INFO_V3_CLICK" && root.TryGetProperty("data", out var click))
        {
            var (mName, mLevel, mColor) = MedalFrom(click, "fans_medal");
            var likeText = GetStr(click, "like_text");
            PushLine(new ChatLine { Type = "like", Name = GetStr(click, "uname"), Text = likeText.Length > 0 ? likeText : "点赞了", MedalName = mName, MedalLevel = mLevel, MedalColor = mColor });
        }
        else if (cmd == "LIKE_INFO_V3_UPDATE" && root.TryGetProperty("data", out var like))
        {
            Likes = like.TryGetProperty("click_count", out var clicks) ? clicks.GetInt32() : Likes;
            StatsChanged?.Invoke();
        }
        else if ((cmd == "ONLINE_RANK_COUNT" || cmd == "ONLINE_RANK_V2") && root.TryGetProperty("data", out var online))
        {
            Online = online.TryGetProperty("online_count", out var oc) ? oc.GetInt32() : online.TryGetProperty("count", out var cc) ? cc.GetInt32() : Online;
            StatsChanged?.Invoke();
        }
        else if (cmd == "WATCHED_CHANGE" && root.TryGetProperty("data", out var watched))
        {
            Watched = watched.TryGetProperty("text_large", out var t) ? t.GetString() ?? Watched : Watched;
            StatsChanged?.Invoke();
        }
    }

    static void PushInteract(JsonElement data)
    {
        // 1.2.0 的映射：1 进场、2 关注、3 分享、4 关注、5 互粉。
        var (type, action) = data.TryGetProperty("msg_type", out var mt) ? mt.GetInt32() switch
        {
            1 => ("enter", "进入直播间"),
            2 => ("follow", "按下了关注"),
            3 => ("share", "分享了直播间"),
            4 => ("follow", "按下了关注"),
            5 => ("follow", "与主播互粉了"),
            _ => ("", "")
        } : ("", "");
        if (type.Length == 0) return;
        // 1.2.0：进场/关注/分享也带粉丝牌（fans_medal）；V1 直接是 JSON 对象，
        // V2 由 DecodeInteract 解出字段 9 拼成同名对象，MedalFrom 一并处理。
        var (mName, mLevel, mColor) = MedalFrom(data, "fans_medal");
        PushLine(new ChatLine { Type = type, Name = GetStr(data, "uname"), Text = action, MedalName = mName, MedalLevel = mLevel, MedalColor = mColor });
    }

    // 极简 protobuf 解码，只取 INTERACT_WORD_V2 需要的字段。
    // 字段 1=uid（varint）、2=uname（bytes）、5=msg_type（varint）、
    // 9=fans_medal（子消息，见 DecodeFansMedal）。
    static JsonElement DecodeInteract(byte[] buf)
    {
        long uid = 0;
        string uname = "";
        long msgType = 0;
        string medalName = "";
        int medalLevel = 0, medalColor = 0;
        var i = 0;
        long Varint()
        {
            long r = 0;
            var s = 0;
            while (i < buf.Length)
            {
                var b = buf[i++];
                r |= (long)(b & 0x7f) << s;
                s += 7;
                if ((b & 0x80) == 0) break;
            }
            return r;
        }
        while (i < buf.Length)
        {
            var key = Varint();
            var no = (int)(key >> 3);
            var wt = (int)(key & 7);
            if (wt == 0)
            {
                var v = Varint();
                if (no == 1) uid = v;
                else if (no == 5) msgType = v;
            }
            else if (wt == 2)
            {
                var len = (int)Varint();
                if (i + len > buf.Length) break;
                if (no == 9)
                {
                    // #21：fans_medal 子消息（1.2.0 注释只确认了 3=medal_name，
                    // 其余子字段按 B 站 FansMedal proto：2=medal_level、4=medal_color_start）。
                    var (mName, mLevel, mColor) = DecodeFansMedal(buf, i, len);
                    medalName = mName; medalLevel = mLevel; medalColor = mColor;
                }
                else
                {
                    var text = Encoding.UTF8.GetString(buf, i, len);
                    if (no == 2) uname = text;
                }
                i += len;
            }
            else if (wt == 1) i += 8;
            else if (wt == 5) i += 4;
            else break;
        }
        using var doc = JsonDocument.Parse($"{{\"uid\":{uid},\"uname\":{JsonSerializer.Serialize(uname)},\"msg_type\":{msgType},\"fans_medal\":{{\"medal_name\":{JsonSerializer.Serialize(medalName)},\"medal_level\":{medalLevel},\"medal_color\":{medalColor}}}}}");
        return doc.RootElement.Clone();
    }

    // #21：INTERACT_WORD_V2 字段 9 fans_medal 的子消息解码。
    // 子字段按 B 站 FansMedal：1=target_id、2=medal_level、3=medal_name、
    // 4=medal_color_start（显示色，JSON 里 medal_color 对应的就是它）。
    // 1.2.0 bili.js 注释只确认了 3=medal_name，2 和 4 需实机验证；
    // 名字或等级为空时 MedalFrom 会丢掉整个牌子，所以解错也只是不显示牌子。
    static (string name, int level, int color) DecodeFansMedal(byte[] buf, int start, int len)
    {
        var name = "";
        long level = 0, color = 0;
        var j = start;
        var end = start + len;
        long Varint()
        {
            long r = 0;
            var s = 0;
            while (j < end)
            {
                var b = buf[j++];
                r |= (long)(b & 0x7f) << s;
                s += 7;
                if ((b & 0x80) == 0) break;
            }
            return r;
        }
        while (j < end)
        {
            var key = Varint();
            var no = (int)(key >> 3);
            var wt = (int)(key & 7);
            if (wt == 0)
            {
                var v = Varint();
                if (no == 2) level = v;
                else if (no == 4) color = v;
            }
            else if (wt == 2)
            {
                var l = (int)Varint();
                if (j + l > end) break;
                if (no == 3) name = Encoding.UTF8.GetString(buf, j, l);
                j += l;
            }
            else if (wt == 1) j += 8;
            else if (wt == 5) j += 4;
            else break;
        }
        return (name, (int)level, (int)color);
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
        req.Headers.TryAddWithoutValidation("User-Agent", $"tokilive/{AppState.Version}");
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
