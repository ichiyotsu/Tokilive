using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Media.Control;

namespace Tokilive;

public sealed class LyricLine
{
    public double Time;
    public string Text = "";
    public string Translation = "";
    // 逐字时间戳（[00:03.00]<00:03.00>不<00:03.40>是）：没有就是空列表。
    // 只解析和保存，逐字高亮是以后再做的事。
    public IReadOnlyList<LyricWord> Words { get; set; } = Array.Empty<LyricWord>();
}

public sealed class LyricWord
{
    public double Time;
    public string Text = "";
}

public sealed class NowPlaying
{
    public string Title = "没有曲目";
    public string Artist = "";
    public string Source = "";
    public string Label = "没有系统媒体会话";
    public double Position;
    public double Duration; // 秒，0 = 未知（直播、EndTime 为 0）
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
    static readonly Dictionary<string, CachedLyrics> Cache = new();
    // 1.2.0 的 DEMO_LRC：4 行，带翻译（| 分隔）和一行逐字时间戳，
    // 用来检查逐行、翻译和逐字解析。最后行 10.50，循环按 14 秒算。
    const string Demo = "[00:00.00]这是 tokilive 的演示歌词 | demo lyric\n[00:03.00]<00:03.00>不<00:03.40>是<00:03.80>在<00:04.20>售<00:04.60>歌<00:05.00>曲 | not a commercial song\n[00:07.00]用来检查逐行、翻译和逐字高亮\n[00:10.50]关闭演示后，这里只显示系统正在播放的曲目";

    public static async Task Refresh()
    {
        if (AppState.Current.LyricDemo)
        {
            Current = new NowPlaying { Title = "演示", Artist = "tokilive", Source = "演示", Label = "演示排版", Lines = Parse(Demo), Position = (DateTime.UtcNow.TimeOfDay.TotalSeconds % 14), Duration = 14, Playing = true };
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
            var duration = Math.Max(0, timeline.EndTime.TotalSeconds);
            var label = Rank(session) == 0 ? "QQ音乐" : Rank(session) == 1 ? "网易云音乐" : "其他来源";
            var (lines, lyricError) = await LyricsFor(props.Title, props.Artist, duration);
            // 歌词查不到/限流时 Label 改成可读的失败原因（设置页和歌词窗
            // 在没有歌词行时显示的就是 Label），而不是悄悄留空行。
            if (lyricError != null) label = lyricError;
            Current = new NowPlaying
            {
                Title = props.Title, Artist = props.Artist, Source = session.SourceAppUserModelId, Label = label,
                Position = position, Duration = duration, Playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
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
        else if (action == "prev" || action == "previous") await session.TrySkipPreviousAsync();
        else if (action == "play") await session.TryPlayAsync();
        else if (action == "pause") await session.TryPauseAsync();
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
            // 先按 | 分出翻译，再只对正文做逐字解析：否则最后一个字的
            // [^<]* 会把 " | 翻译" 也吞进 Words 里（1.2.0 parseLrc 的
            // 同款问题）。翻译里的尖括号标签也顺手去掉。
            var parts = body.Split('|');
            body = parts[0].Trim();
            var translation = parts.Length > 1 ? Regex.Replace(parts[1], "<[^>]*>", "").Trim() : "";
            // 逐字时间戳（1.2.0 lrc.js 的 parseLrc）：<mm:ss.xx>字 里的字
            // 拼回纯文本，时间戳留到 Words 里；行里没有就是空列表。
            var words = new List<LyricWord>();
            if (body.Contains('<'))
            {
                var plain = "";
                foreach (Match word in Regex.Matches(body, @"<(\d+):(\d+(?:\.\d+)?)>([^<]*)"))
                {
                    var w = word.Groups[3].Value;
                    if (w.Length == 0) continue;
                    words.Add(new LyricWord { Time = int.Parse(word.Groups[1].Value) * 60 + double.Parse(word.Groups[2].Value), Text = w });
                    plain += w;
                }
                body = plain.Length > 0 ? plain : Regex.Replace(body, "<[^>]*>", "");
            }
            foreach (Match stamp in stamps)
                lines.Add(new LyricLine { Time = int.Parse(stamp.Groups[1].Value) * 60 + double.Parse(stamp.Groups[2].Value), Text = body, Translation = translation, Words = words });
        }
        return lines.OrderBy(l => l.Time).ToList();
    }

    // 导入校验给设置页用：文件里没有带时间的行就返回 false，
    // 调用方拿 lineCount 拼“文件里没有带时间的歌词行”这类提示。
    public static bool TryParseLyrics(string text, out int lineCount)
    {
        lineCount = string.IsNullOrWhiteSpace(text) ? 0 : Parse(text).Count;
        return lineCount > 0;
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

    // 返回 (歌词行, 失败原因)。失败原因不为空时 Refresh 会把它放到
    // NowPlaying.Label 上显示；成功时失败原因为空，Label 保持来源名。
    static async Task<(IReadOnlyList<LyricLine> Lines, string? Error)> LyricsFor(string title, string artist, double duration)
    {
        if (AppState.Current.LyricLocal.Length > 0) return (Parse(AppState.Current.LyricLocal), null);
        var key = title + "\n" + artist;
        if (!Cache.TryGetValue(key, out var cached))
        {
            cached = await FetchLyrics(title, artist);
            if (cached.Cacheable) Cache[key] = cached;
        }
        if (cached.Error != null) return (Array.Empty<LyricLine>(), cached.Error);
        return (cached.Plain ? ParsePlain(cached.Text, duration) : Parse(cached.Text), null);
    }

    // lrclib 查询：只有 HTTP 200 的结果才进缓存，429/404/其他非 200
    // 和网络异常都不缓存，下次 Refresh 会再试一次。
    static async Task<CachedLyrics> FetchLyrics(string title, string artist)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"tokilive/{AppState.Version} (https://github.com/ichiyotsu/Tokilive)");
            var url = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(title) + "&artist_name=" + Uri.EscapeDataString(artist);
            using var res = await http.GetAsync(url);
            if ((int)res.StatusCode == 429) return new CachedLyrics { Error = "歌词服务限流，稍后再试", Cacheable = false };
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return new CachedLyrics { Error = "没找到这首歌的歌词", Cacheable = false };
            if (!res.IsSuccessStatusCode) return new CachedLyrics { Error = "歌词服务返回 HTTP " + (int)res.StatusCode, Cacheable = false };
            using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var hit = PickRecord(json.RootElement, title, artist);
            if (hit == null) return new CachedLyrics { Error = "没找到这首歌的歌词" };
            // 1.2.0 lyrics-now 的 synced || plain：没有同步歌词时
            // 也显示纯文本歌词，而不是留空。
            if (!string.IsNullOrWhiteSpace(hit.Value.Synced)) return new CachedLyrics { Text = hit.Value.Synced };
            return new CachedLyrics { Text = hit.Value.Plain, Plain = true };
        }
        catch { return new CachedLyrics { Error = "歌词服务不可用，稍后再试", Cacheable = false }; }
    }

    // 1.2.0 lrclib.js 的 pickRecord：只要有 synced 或 plain 就算候选，
    // 有同步歌词的优先，歌名/歌手精确匹配的加分。
    static (string Synced, string Plain)? PickRecord(JsonElement root, string title, string artist)
    {
        if (root.ValueKind != JsonValueKind.Array) return null;
        var wantedTitle = title.Trim().ToLowerInvariant();
        var wantedArtist = artist.Trim().ToLowerInvariant();
        (string Synced, string Plain, int Score)? best = null;
        foreach (var el in root.EnumerateArray())
        {
            var synced = Str(el, "syncedLyrics");
            var plain = Str(el, "plainLyrics");
            if (string.IsNullOrWhiteSpace(synced) && string.IsNullOrWhiteSpace(plain)) continue;
            var score = string.IsNullOrWhiteSpace(synced) ? 1 : 2;
            if (wantedTitle.Length > 0 && Str(el, "trackName").Trim().ToLowerInvariant() == wantedTitle) score += 3;
            if (wantedArtist.Length > 0 && Str(el, "artistName").Trim().ToLowerInvariant() == wantedArtist) score += 2;
            if (best == null || score > best.Value.Score) best = (synced, plain, score);
        }
        return best == null ? null : (best.Value.Synced, best.Value.Plain);
    }

    static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // plainLyrics 没有时间轴：按总时长把行均匀摊开，让纯文本能跟着
    // 进度走；不知道总时长时每行隔 5 秒。时间是估算的，不是真实时间戳。
    static IReadOnlyList<LyricLine> ParsePlain(string text, double duration)
    {
        var rows = text.Replace("\r", "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (rows.Count == 0) return Array.Empty<LyricLine>();
        var step = duration > 0 ? duration / rows.Count : 5;
        var lines = new List<LyricLine>();
        for (var i = 0; i < rows.Count; i++)
            lines.Add(new LyricLine { Time = i * step, Text = rows[i] });
        return lines;
    }

    static int Rank(GlobalSystemMediaTransportControlsSession session)
    {
        var id = session.SourceAppUserModelId ?? "";
        if (id.Contains("QQMusic", StringComparison.OrdinalIgnoreCase) || id.Contains("QQ音乐", StringComparison.Ordinal)) return 0;
        if (id.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) || id.Contains("neteasemusic", StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    // 一次 lrclib 查询的缓存项：Text 是选中的 syncedLyrics，
    // 没有同步歌词时是 plainLyrics（Plain = true，按时长摊开显示）；
    // Error 有值表示这次查询没有可用歌词，Lines 为空。
    // Cacheable = false 的是 HTTP 失败/网络异常，不进缓存。
    sealed class CachedLyrics
    {
        public string Text = "";
        public bool Plain;
        public string? Error;
        public bool Cacheable = true;
    }
}
