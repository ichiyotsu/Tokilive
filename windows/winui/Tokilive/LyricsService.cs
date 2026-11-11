using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Media.Control;

namespace Tokilive;

public sealed class LyricLine
{
    public double Time;
    public string Text = "";
    public string Translation = "";
}

public sealed class NowPlaying
{
    public string Title = "没有曲目";
    public string Artist = "";
    public string Source = "";
    public string Label = "没有系统媒体会话";
    public double Position;
    public bool Playing;
    public string Current = "";
    public IReadOnlyList<LyricLine> Lines = Array.Empty<LyricLine>();
}

public static class LyricsService
{
    public static NowPlaying Current { get; private set; } = new();
    public static event Action? Updated;
    static GlobalSystemMediaTransportControlsSessionManager? _manager;
    // 当前订阅事件的会话：会话没了（播放器退出）就换一个重新订，
    // 否则切歌/暂停只靠定时轮询才知道。
    static GlobalSystemMediaTransportControlsSession? _session;
    static bool _managerWired;
    static readonly object _watchLock = new();
    static readonly Dictionary<string, string> Cache = new();
    const string Demo = "[00:00.00]这是 tokilive 自己写的演示词\n[00:03.00]不是你正在听的歌\n[00:06.00]用来看桌面歌词的排版";

    public static async Task Refresh()
    {
        if (AppState.Current.LyricDemo)
        {
            Current = new NowPlaying { Title = "演示", Artist = "tokilive", Source = "演示", Label = "演示排版", Lines = Parse(Demo), Position = (DateTime.UtcNow.TimeOfDay.TotalSeconds % 9), Playing = true };
            Current.Current = LineAt(Current.Lines, Current.Position);
            Updated?.Invoke();
            return;
        }
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = _manager.GetSessions();
            var session = sessions.OrderBy(Rank).FirstOrDefault();
            Watch(_manager, session);
            if (session == null)
            {
                Current = new NowPlaying { Label = "没有系统媒体会话。QQ 音乐和网易云需要向系统报告正在播放的曲目。" };
                Updated?.Invoke();
                return;
            }
            var props = await session.TryGetMediaPropertiesAsync();
            var timeline = session.GetTimelineProperties();
            var status = session.GetPlaybackInfo().PlaybackStatus;
            var position = timeline.Position.TotalSeconds;
            if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                position += Math.Max(0, (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds);
            position += AppState.Current.LyricOffset;
            var label = Rank(session) == 0 ? "QQ音乐" : Rank(session) == 1 ? "网易云音乐" : "其他来源";
            var lines = await LyricsFor(props.Title, props.Artist);
            Current = new NowPlaying
            {
                Title = props.Title, Artist = props.Artist, Source = session.SourceAppUserModelId, Label = label,
                Position = position, Playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Lines = lines, Current = LineAt(lines, position)
            };
        }
        catch (Exception ex)
        {
            Current = new NowPlaying { Label = "读不到系统媒体会话：" + ex.Message };
        }
        Updated?.Invoke();
    }

    // SMTC 事件：切歌、暂停、进度条拖动立刻触发 Refresh，不用等
    // 下一次轮询。500ms 的定时器还留着做兜底（时间轴在走、事件丢了、
    // 或者会话换了但 CurrentSessionChanged 没送到）。
    // 事件在后台线程上触发，Refresh 里只改 Current 并发 Updated，
    // 界面那边的订阅者（MainWindow）负责切回 UI 线程。
    static void Watch(GlobalSystemMediaTransportControlsSessionManager manager, GlobalSystemMediaTransportControlsSession? session)
    {
        lock (_watchLock)
        {
            if (!_managerWired)
            {
                manager.CurrentSessionChanged += async (_, _) => { try { await Refresh(); } catch { } };
                _managerWired = true;
            }
            // 同一个会话不要重复订：GetSessions 每次返回的包装通常是
            // 同一个 RCW，ReferenceEquals 能认出来；认不出来就重订，
            // 先退订旧的，不会越积越多。
            if (ReferenceEquals(_session, session)) return;
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnSessionChanged;
                _session.TimelinePropertiesChanged -= OnSessionChanged;
                _session.PlaybackInfoChanged -= OnSessionChanged;
            }
            _session = session;
            if (_session == null) return;
            _session.MediaPropertiesChanged += OnSessionChanged;
            _session.TimelinePropertiesChanged += OnSessionChanged;
            _session.PlaybackInfoChanged += OnSessionChanged;
        }
    }

    static async void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        try { await Refresh(); } catch { }
    }

    public static async Task Control(string action)
    {
        if (_manager == null) return;
        var session = _manager.GetSessions().OrderBy(Rank).FirstOrDefault();
        if (session == null) return;
        if (action == "next") await session.TrySkipNextAsync();
        else if (action == "prev") await session.TrySkipPreviousAsync();
        else await session.TryTogglePlayPauseAsync();
    }

    public static IReadOnlyList<LyricLine> Parse(string text)
    {
        if (text.Contains("<tt", StringComparison.OrdinalIgnoreCase) || text.Contains("<p ", StringComparison.OrdinalIgnoreCase))
            return ParseTtml(text);
        var lines = new List<LyricLine>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var stamps = Regex.Matches(raw, @"\[(\d+):(\d+(?:\.\d+)?)\]");
            if (stamps.Count == 0) continue;
            var body = Regex.Replace(raw, @"\[(\d+):(\d+(?:\.\d+)?)\]", "").Trim();
            var parts = body.Split('|');
            foreach (Match stamp in stamps)
                lines.Add(new LyricLine { Time = int.Parse(stamp.Groups[1].Value) * 60 + double.Parse(stamp.Groups[2].Value), Text = parts[0].Trim(), Translation = parts.Length > 1 ? parts[1].Trim() : "" });
        }
        return lines.OrderBy(l => l.Time).ToList();
    }

    static IReadOnlyList<LyricLine> ParseTtml(string xml)
    {
        var lines = new List<LyricLine>();
        foreach (Match match in Regex.Matches(xml, @"<p\b([^>]*)>([\s\S]*?)</p>", RegexOptions.IgnoreCase))
        {
            var begin = Regex.Match(match.Groups[1].Value, @"begin=""([^""]+)""");
            if (!begin.Success) continue;
            var text = Regex.Replace(match.Groups[2].Value, "<[^>]+>", "").Trim();
            if (text.Length == 0) continue;
            lines.Add(new LyricLine { Time = Clock(begin.Groups[1].Value), Text = text });
        }
        return lines.OrderBy(l => l.Time).ToList();
    }

    static double Clock(string value)
    {
        var parts = value.Split(':').Select(s => double.TryParse(s, out var n) ? n : 0).ToArray();
        if (parts.Length == 3) return parts[0] * 3600 + parts[1] * 60 + parts[2];
        if (parts.Length == 2) return parts[0] * 60 + parts[1];
        return parts.FirstOrDefault();
    }

    static string LineAt(IReadOnlyList<LyricLine> lines, double at)
    {
        LyricLine? hit = null;
        foreach (var line in lines) if (line.Time <= at) hit = line; else break;
        return hit == null ? "" : hit.Text;
    }

    static async Task<IReadOnlyList<LyricLine>> LyricsFor(string title, string artist)
    {
        if (AppState.Current.LyricLocal.Length > 0) return Parse(AppState.Current.LyricLocal);
        var key = title + "\n" + artist;
        if (!Cache.TryGetValue(key, out var text))
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "tokilive/1.3.1 (https://github.com/ichiyotsu/Tokilive)");
                var url = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(title) + "&artist_name=" + Uri.EscapeDataString(artist);
                var json = JsonDocument.Parse(await http.GetStringAsync(url));
                text = json.RootElement.GetArrayLength() == 0 ? "" : json.RootElement[0].TryGetProperty("syncedLyrics", out var synced) ? synced.GetString() ?? "" : "";
            }
            catch { text = ""; }
            Cache[key] = text;
        }
        return Parse(text);
    }

    static int Rank(GlobalSystemMediaTransportControlsSession session)
    {
        var id = session.SourceAppUserModelId ?? "";
        if (id.Contains("QQMusic", StringComparison.OrdinalIgnoreCase) || id.Contains("QQ音乐", StringComparison.Ordinal)) return 0;
        if (id.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) || id.Contains("neteasemusic", StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }
}
