using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Tokilive;

// OBS 浏览器源实时推送（对齐 1.2.0 的 SSE 协议）。
//
// 1.2.0（windows/src/main.js broadcast() / obs-routes.js）：
//   - GET /events 是 text/event-stream，帧格式固定为
//     `event: <ch>\ndata: <json>\n\n`，频道有 config / stats / message；
//   - 连接时立即发 config（mini 再补发 stats），断开时把客户端从 sseClients 移除；
//   - /overlay 用主弹幕流，/mini 用互动小窗流（?w=mini 区分）。
//
// WinUI 实现要点：
//   - 只订阅 LiveService / AppState 上已经公开的事件（Lines/Pills 的
//     CollectionChanged、StatsChanged、AppState.Changed），不给 LiveService
//     加新事件；CollectionChanged 在 UI 线程触发，所以事件处理里只做
//     “序列化一帧 + 塞进各客户端队列”，网络写由每客户端的后台发送循环做，
//     绝不在 UI 线程 / Accept 循环里同步写流。
//   - 每客户端一个发送循环 + 30 秒心跳注释（": ping"），写失败就移除该客户端，
//     一个死客户端不影响服务器和其他客户端。
//   - 字体不提供：WinUI 不打包 woff2 字体，没有东西可 serve。
public static class ObsServer
{
    static HttpListener? _listener;

    // 线程安全的已连接 SSE 客户端集。
    static readonly ConcurrentDictionary<SseClient, byte> _clients = new();

    // Apply() 会被反复调用（开关/端口变化），订阅只做一次，防止重复推送。
    static bool _subscribed;
    static readonly object _subLock = new();

    // stats / config 可能短时间内连变多次（点赞、状态刷新），400ms 合并一次，
    // 发的时候读最新快照；做法和 1.2.0 main.js 的 bumpStats() 一致。
    static int _statsPending;
    static int _configPending;

    public static void Apply()
    {
        Stop();
        if (!AppState.Current.ObsEnabled) return;
        var port = Math.Clamp(AppState.Current.ObsPort, 1024, 65535);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try { _listener.Start(); }
        catch (Exception ex) { AppState.Current.Status = "本地页面没有打开：" + ex.Message; return; }
        EnsureSubscribed();
        _ = Accept(_listener);
    }

    public static void Stop()
    {
        // 先断掉所有 SSE 长连接（否则旧客户端会一直挂着），再停监听。
        foreach (var c in _clients.Keys) c.Close();
        _clients.Clear();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    public static string OverlayUrl => $"http://127.0.0.1:{AppState.Current.ObsPort}/overlay";
    public static string MiniUrl => $"http://127.0.0.1:{AppState.Current.ObsPort}/mini";

    static void EnsureSubscribed()
    {
        lock (_subLock)
        {
            if (_subscribed) return;
            _subscribed = true;
            LiveService.Lines.CollectionChanged += OnLinesChanged;
            LiveService.Pills.CollectionChanged += OnPillsChanged;
            LiveService.StatsChanged += OnStatsChanged;
            AppState.Current.Changed += OnConfigChanged;
        }
    }

    // ---- 已有事件的转发（只入队，不写网络）----

    static void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => PushNew(e, "main");
    static void OnPillsChanged(object? sender, NotifyCollectionChangedEventArgs e) => PushNew(e, "mini");

    static void PushNew(NotifyCollectionChangedEventArgs e, string target)
    {
        if (e.NewItems == null || _clients.IsEmpty) return;
        foreach (var item in e.NewItems)
        {
            if (item is not ChatLine line) continue;
            // 和 /api/state 里 lines/pills 的字段形状一致：Type/Name/Text。
            Broadcast(SseFrame("message", JsonSerializer.Serialize(new { line.Type, line.Name, line.Text })), target);
        }
    }

    static void OnStatsChanged() => ScheduleStats();
    static void OnConfigChanged() => ScheduleConfig();

    // 去抖辅助：直接读写各自的静态字段（不能把 ref 字段传进 lambda）。
    static void ScheduleStats()
    {
        if (Interlocked.Exchange(ref _statsPending, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(400); }
            catch { /* 进程退出时 Delay 被打断就直接发 */ }
            Interlocked.Exchange(ref _statsPending, 0);
            try { Broadcast(SseFrame("stats", StatsJson())); }
            catch { /* 广播只入队，理论上不抛；兜底不让后台任务崩掉 */ }
        });
    }

    static void ScheduleConfig()
    {
        if (Interlocked.Exchange(ref _configPending, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(400); }
            catch { /* 进程退出时 Delay 被打断就直接发 */ }
            Interlocked.Exchange(ref _configPending, 0);
            try { Broadcast(SseFrame("config", ConfigJson())); }
            catch { /* 广播只入队，理论上不抛；兜底不让后台任务崩掉 */ }
        });
    }

    // ---- 广播 ----

    // target：和 1.2.0 broadcast() 一致，'all' | 'main' | 'mini'。
    static void Broadcast(string frame, string target = "all")
    {
        foreach (var c in _clients.Keys)
        {
            if (target == "main" && c.Mini) continue;
            if (target == "mini" && !c.Mini) continue;
            c.Enqueue(frame);
        }
    }

    // 1.2.0 的帧格式：`event: <ch>\ndata: <json>\n\n`。
    static string SseFrame(string channel, string json) => $"event: {channel}\ndata: {json}\n\n";

    static string StatsJson() => JsonSerializer.Serialize(new
    {
        LiveService.Likes, LiveService.Danmaku, LiveService.Online, LiveService.Watched
    });

    // 配置快照：给浏览器源用的主题值（卡片/名字/正文颜色、字号、圆角、
    // 不透明度、字体、字重、磨砂）。字段名用小驼峰，页面直接读。
    static string ConfigJson() => JsonSerializer.Serialize(new
    {
        cardColor = AppState.Current.CardColor,
        nameColor = AppState.Current.NameColor,
        textColor = AppState.Current.TextColor,
        fontSize = AppState.Current.FontSize,
        corner = AppState.Current.Corner,
        cardOpacity = AppState.Current.CardOpacity,
        cardFont = AppState.Current.CardFont,
        fontWeight = AppState.Current.DanmakuFontWeight,
        cardBlur = AppState.Current.CardBlur,
        themeMode = AppState.Current.CardThemeMode,
        seedColor = AppState.Current.SeedColor,
        showAvatar = AppState.Current.ShowAvatar
    });

    static async Task Accept(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { return; }
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (path == "/events")
            {
                // 长连接绝不能在 Accept 循环里 await：丢给后台任务，循环继续接下一个请求。
                _ = HandleEventsAsync(ctx);
                continue;
            }
            var body = path == "/api/state" ? Json() : Page(path == "/mini");
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentType = path == "/api/state" ? "application/json" : "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }

    static async Task HandleEventsAsync(HttpListenerContext ctx)
    {
        // 1.2.0 用 ?w=mini 区分小窗；/overlay 不带参数就是主弹幕流。
        var mini = (ctx.Request.Url?.Query ?? "").Contains("w=mini", StringComparison.OrdinalIgnoreCase);
        var client = new SseClient(ctx.Response, mini);
        try
        {
            _clients[client] = 0;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.SendChunked = true;
            // 先注册进 _clients 再排初始帧：队列是 FIFO，初始 state 是全量快照，
            // 页面收到 state 会整页重绘，所以即使某条新消息排在 state 帧前面，
            // 也会被快照覆盖，不会重复；排在后面的则是快照之后的新消息。
            client.Enqueue(SseFrame("config", ConfigJson()));
            client.Enqueue(SseFrame("state", Json()));
            client.Enqueue(SseFrame("stats", StatsJson()));
            await client.RunAsync();
        }
        catch { /* 连接建立失败也只影响这一个客户端 */ }
        finally
        {
            _clients.TryRemove(client, out _);
            client.Close();
        }
    }

    // /api/state 的 JSON 形状保持不变（已有客户端可能依赖它）。
    static string Json() => JsonSerializer.Serialize(new
    {
        lines = LiveService.Lines.TakeLast(30).Select(l => new { l.Type, l.Name, l.Text }),
        pills = LiveService.Pills.TakeLast(30).Select(l => new { l.Type, l.Name, l.Text }),
        LiveService.Likes, LiveService.Danmaku, LiveService.Online, LiveService.Watched
    });

    static string Page(bool mini)
    {
        // __TITLE__ / __MINI__ / __FIELD__ 三处占位由下面 Replace 填入。
        // 页面逻辑：优先走 SSE（/events），EventSource 出错/不可用时回落到
        // 原来的 1 秒 /api/state 轮询；EventSource 重连成功（onopen）后停掉轮询。
        // 卡片样式不再写死白色，改用 config 事件下发的主题变量。
        var html = """
            <!doctype html><meta charset=utf-8><title>tokilive · __TITLE__</title>
            <style>
            :root{--card-bg:rgba(255,255,255,.88);--card-text:#111;--card-name:#111;--card-fs:16px;--card-radius:16px;--card-font:'Segoe UI Variable',sans-serif}
            html,body{margin:0;background:transparent;color:var(--card-text);font-family:var(--card-font);font-size:var(--card-fs)}
            .card{margin:8px;padding:10px 14px;border-radius:var(--card-radius);background:var(--card-bg);color:var(--card-text)}
            .card b{color:var(--card-name)}
            #stats{margin:8px;padding:10px 14px;border-radius:var(--card-radius);background:var(--card-bg);color:var(--card-text)}
            </style>
            <div id=stats></div><div id=root></div><script>
            function esc(s){return String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]))}
            var mini=__MINI__;var field='__FIELD__';var items=[];var stats={};
            function rgba(hex,a){var m=/^#?([0-9a-f]{6})$/i.exec(String(hex||'').trim());if(!m)return null;var n=parseInt(m[1],16);return 'rgba('+((n>>16)&255)+','+((n>>8)&255)+','+(n&255)+','+a+')'}
            function applyConfig(c){if(!c)return;if(c.cfg)c=c.cfg;var r=document.documentElement.style;var bg=rgba(c.cardColor,c.cardOpacity==null?0.88:c.cardOpacity);if(bg)r.setProperty('--card-bg',bg);if(c.textColor)r.setProperty('--card-text',c.textColor);if(c.nameColor)r.setProperty('--card-name',c.nameColor);if(c.fontSize)r.setProperty('--card-fs',c.fontSize+'px');if(c.corner!=null)r.setProperty('--card-radius',c.corner+'px');if(c.cardFont)r.setProperty('--card-font',"'"+String(c.cardFont).replace(/'/g,'')+"',sans-serif")}
            function render(){var root=document.querySelector('#root');root.innerHTML=items.slice(-12).map(x=>'<div class="card"><b>'+esc(x.Name)+'</b> '+esc(x.Text)+'</div>').join('')||'<div class="card">等待消息</div>';var st=document.querySelector('#stats');if(!mini){st.style.display='none';return}st.style.display='';st.innerHTML='点赞 '+(stats.Likes??'—')+' · 弹幕 '+(stats.Danmaku??'—')+' · 在线 '+(stats.Online??'—')+(stats.Watched?'<br>'+esc(stats.Watched):'')}
            function applyState(s){if(!s)return;items=(s[field]||[]).slice(-12);stats={Likes:s.Likes,Danmaku:s.Danmaku,Online:s.Online,Watched:s.Watched};render()}
            function applyStats(s){if(!s)return;stats=s;render()}
            function addMessage(m){if(!m)return;items.push(m);if(items.length>12)items.splice(0,items.length-12);render()}
            async function tick(){try{applyState(await fetch('/api/state').then(r=>r.json()))}catch(e){}}
            var pollTimer=null;
            function startPoll(){if(pollTimer)return;tick();pollTimer=setInterval(tick,1000)}
            function stopPoll(){if(pollTimer){clearInterval(pollTimer);pollTimer=null}}
            if(typeof EventSource==='undefined'){startPoll()}
            else{var es=new EventSource('/events'+(mini?'?w=mini':''));es.addEventListener('config',function(e){try{applyConfig(JSON.parse(e.data))}catch(err){}});es.addEventListener('state',function(e){try{applyState(JSON.parse(e.data))}catch(err){}});es.addEventListener('stats',function(e){try{applyStats(JSON.parse(e.data))}catch(err){}});es.addEventListener('message',function(e){try{addMessage(JSON.parse(e.data))}catch(err){}});es.onopen=function(){stopPoll()};es.onerror=function(){startPoll()}}
            tick();
            </script>
            """;
        return html.Replace("__TITLE__", mini ? "实时窗" : "弹幕")
            .Replace("__MINI__", mini ? "true" : "false")
            .Replace("__FIELD__", mini ? "pills" : "lines");
    }

    // 一个 SSE 客户端：生产者（事件处理）只 Enqueue，网络写只发生在这个
    // 类的后台发送循环里，所以慢/死的客户端不会拖住 UI 线程、Accept 循环
    // 或其他客户端。写流抛异常（对端断开）即结束循环，由调用方移除。
    sealed class SseClient(HttpListenerResponse response, bool mini)
    {
        public bool Mini { get; } = mini;
        readonly ConcurrentQueue<string> _queue = new();
        readonly SemaphoreSlim _signal = new(0);
        readonly CancellationTokenSource _cts = new();
        int _closed;

        public void Enqueue(string frame)
        {
            if (_closed == 1) return;
            _queue.Enqueue(frame);
            try { _signal.Release(); }
            catch { /* 已关闭时 Release 会抛，忽略 */ }
        }

        public async Task RunAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    bool signaled;
                    try { signaled = await _signal.WaitAsync(TimeSpan.FromSeconds(30), _cts.Token); }
                    catch (OperationCanceledException) { break; }
                    if (!signaled)
                    {
                        // 30 秒心跳注释：保活，也能早点发现已死的连接。
                        await WriteAsync(": ping\n\n");
                        continue;
                    }
                    while (_queue.TryDequeue(out var frame)) await WriteAsync(frame);
                }
            }
            catch (Exception) { /* 客户端断开 / 写失败：只结束这一个连接 */ }
        }

        async Task WriteAsync(string frame)
        {
            var bytes = Encoding.UTF8.GetBytes(frame);
            await response.OutputStream.WriteAsync(bytes, _cts.Token);
            await response.OutputStream.FlushAsync(_cts.Token);
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            try { _cts.Cancel(); } catch { }
            try { _signal.Release(); } catch { }
            try { response.Close(); } catch { }
        }
    }
}
