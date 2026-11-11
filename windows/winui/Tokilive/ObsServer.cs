using System.Net;
using System.Text;
using System.Text.Json;

namespace Tokilive;

public static class ObsServer
{
    static HttpListener? _listener;

    public static void Apply()
    {
        Stop();
        if (!AppState.Current.ObsEnabled) return;
        var port = Math.Clamp(AppState.Current.ObsPort, 1024, 65535);
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try { _listener.Start(); }
        catch (Exception ex) { AppState.Current.Status = "本地页面没有打开：" + ex.Message; return; }
        _ = Accept(_listener);
    }

    public static void Stop()
    {
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    public static string OverlayUrl => $"http://127.0.0.1:{AppState.Current.ObsPort}/overlay";
    public static string MiniUrl => $"http://127.0.0.1:{AppState.Current.ObsPort}/mini";

    static async Task Accept(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { return; }
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var body = path == "/api/state" ? Json() : Page(path == "/mini");
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentType = path == "/api/state" ? "application/json" : "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }

    static string Json() => JsonSerializer.Serialize(new
    {
        lines = LiveService.Lines.TakeLast(30).Select(l => new { l.Type, l.Name, l.Text }),
        pills = LiveService.Pills.TakeLast(30).Select(l => new { l.Type, l.Name, l.Text }),
        LiveService.Likes, LiveService.Danmaku, LiveService.Online, LiveService.Watched
    });

    static string Page(bool mini)
    {
        var pick = mini ? "s.pills" : "s.lines";
        var stats = mini ? "root.insertAdjacentHTML('afterbegin', `<div class=\"card\">点赞 ${s.Likes} · 弹幕 ${s.Danmaku} · 在线 ${s.Online}<br>${esc(s.Watched||'')}</div>`);" : "";
        return "<!doctype html><meta charset=utf-8><title>tokilive · " + (mini ? "实时窗" : "弹幕") + "</title>"
            + "<style>html,body{margin:0;background:transparent;color:#111;font:16px 'Segoe UI Variable',sans-serif}.card{margin:8px;padding:10px 14px;border-radius:16px;background:rgba(255,255,255,.88)}</style>"
            + "<div id=root></div><script>function esc(s){return String(s??'').replace(/[&<>\"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;',\"'\":'&#39;'}[c]))}async function tick(){const s=await fetch('/api/state').then(r=>r.json());const root=document.querySelector('#root');"
            + "const rows=" + pick + ".slice(-12).map(x=>'<div class=\"card\"><b>'+esc(x.Name)+'</b> '+esc(x.Text)+'</div>').join('');"
            + "root.innerHTML=rows||'<div class=\"card\">等待消息</div>';" + stats + "}setInterval(tick,1000);tick();</script>";
    }
}
