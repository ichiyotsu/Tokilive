using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tokilive;

public sealed class AppState : INotifyPropertyChanged
{
    public const string Version = "1.3.1";
    public static AppState Current { get; } = Load();

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tokilive", "config.json");

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    public string Room { get => room; set => Set(ref room, value); }
    public string Cookie { get => cookie; set => Set(ref cookie, value); }
    public int ThemeMode { get => themeMode; set => Set(ref themeMode, value); }
    public bool UseSystemAccent { get => useSystemAccent; set => Set(ref useSystemAccent, value); }
    public string AccentHex { get => accentHex; set => Set(ref accentHex, value); }
    public bool ClickThrough { get => clickThrough; set => Set(ref clickThrough, value); }
    public bool ShowDanmaku { get => showDanmaku; set => Set(ref showDanmaku, value); }
    public bool ShowLive { get => showLive; set => Set(ref showLive, value); }
    public bool ShowDesktopLyrics { get => showDesktopLyrics; set => Set(ref showDesktopLyrics, value); }
    public bool ShowAvatar { get => showAvatar; set => Set(ref showAvatar, value); }
    public string NameColor { get => nameColor; set => Set(ref nameColor, value); }
    public string TextColor { get => textColor; set => Set(ref textColor, value); }
    public string CardColor { get => cardColor; set => Set(ref cardColor, value); }
    public double Corner { get => corner; set => Set(ref corner, value); }
    public double FontSize { get => fontSize; set => Set(ref fontSize, value); }
    public double CardOpacity { get => cardOpacity; set => Set(ref cardOpacity, value); }
    public bool AdaptiveText { get => adaptiveText; set => Set(ref adaptiveText, value); }
    public bool TtsEnabled { get => ttsEnabled; set => Set(ref ttsEnabled, value); }
    public double TtsRate { get => ttsRate; set => Set(ref ttsRate, value); }
    public string TtsVoice { get => ttsVoice; set => Set(ref ttsVoice, value); }
    public string Hotkey { get => hotkey; set => Set(ref hotkey, value); }
    public bool InputLocked { get => inputLocked; set => Set(ref inputLocked, value); }
    public bool HideFromTaskbar { get => hideFromTaskbar; set => Set(ref hideFromTaskbar, value); }
    public bool ObsEnabled { get => obsEnabled; set => Set(ref obsEnabled, value); }
    public int ObsPort { get => obsPort; set => Set(ref obsPort, value); }
    public bool ShowWindowBorder { get => showWindowBorder; set => Set(ref showWindowBorder, value); }
    public bool ShowDanmakuMsg { get => showDanmakuMsg; set => Set(ref showDanmakuMsg, value); }
    public bool ShowGift { get => showGift; set => Set(ref showGift, value); }
    public bool ShowSuperchat { get => showSuperchat; set => Set(ref showSuperchat, value); }
    public bool ShowGuard { get => showGuard; set => Set(ref showGuard, value); }
    public bool ShowEnter { get => showEnter; set => Set(ref showEnter, value); }
    public bool ShowFollow { get => showFollow; set => Set(ref showFollow, value); }
    public bool ShowLike { get => showLike; set => Set(ref showLike, value); }
    public bool ShowShare { get => showShare; set => Set(ref showShare, value); }
    // 弹幕动画：总开关、预设（default/blur/pop/rise/type）、速度倍率。
    // 关掉或选默认时，新弹幕直接出现，和现在一样，没有入场动画。
    public bool DanmakuAnimEnabled { get => danmakuAnimEnabled; set => Set(ref danmakuAnimEnabled, value); }
    public string DanmakuAnim { get => danmakuAnim; set => Set(ref danmakuAnim, value); }
    public double DanmakuAnimSpeed { get => danmakuAnimSpeed; set => Set(ref danmakuAnimSpeed, value); }
    // 1.2.0 main.js 默认 minGiftPrice: 0（元）。>0 时按 overlay.js 的规则
    // 过滤便宜礼物；没有给它做设置 UI，先只留字段和过滤逻辑。
    public double MinGiftPrice { get => minGiftPrice; set => Set(ref minGiftPrice, value); }
    public double TtsVolume { get => ttsVolume; set => Set(ref ttsVolume, value); }
    public string CardFont { get => cardFont; set => Set(ref cardFont, value); }
    public bool ExperimentalTopmost { get => experimentalTopmost; set => Set(ref experimentalTopmost, value); }
    public int DanmakuX { get => danmakuX; set => Set(ref danmakuX, value); }
    public int DanmakuY { get => danmakuY; set => Set(ref danmakuY, value); }
    public int DanmakuW { get => danmakuW; set => Set(ref danmakuW, value); }
    public int DanmakuH { get => danmakuH; set => Set(ref danmakuH, value); }
    public int LiveX { get => liveX; set => Set(ref liveX, value); }
    public int LiveY { get => liveY; set => Set(ref liveY, value); }
    public int LiveW { get => liveW; set => Set(ref liveW, value); }
    public int LiveH { get => liveH; set => Set(ref liveH, value); }
    public int LyricX { get => lyricX; set => Set(ref lyricX, value); }
    public int LyricY { get => lyricY; set => Set(ref lyricY, value); }
    public int LyricW { get => lyricW; set => Set(ref lyricW, value); }
    public int LyricH { get => lyricH; set => Set(ref lyricH, value); }
    public bool ShowLiveBorder { get => showLiveBorder; set => Set(ref showLiveBorder, value); }
    public string LyricFont { get => lyricFont; set => Set(ref lyricFont, value); }
    public double LyricSize { get => lyricSize; set => Set(ref lyricSize, value); }
    public string LyricColor { get => lyricColor; set => Set(ref lyricColor, value); }
    public string LyricHighlight { get => lyricHighlight; set => Set(ref lyricHighlight, value); }
    public string LyricAlign { get => lyricAlign; set => Set(ref lyricAlign, value); }
    public bool LyricShadow { get => lyricShadow; set => Set(ref lyricShadow, value); }
    public bool LyricOutline { get => lyricOutline; set => Set(ref lyricOutline, value); }
    public double LyricBg { get => lyricBg; set => Set(ref lyricBg, value); }
    public int LyricLines { get => lyricLines; set => Set(ref lyricLines, value); }
    // 歌词新字段：粗细（light/normal/bold/black）、单双行（single/double）、
    // 横竖排（horizontal/vertical）、设计（default/cyberpunk/minecraft）。
    public string LyricWeight { get => lyricWeight; set => Set(ref lyricWeight, value); }
    public string LyricMode { get => lyricMode; set => Set(ref lyricMode, value); }
    public string LyricDirection { get => lyricDirection; set => Set(ref lyricDirection, value); }
    public string LyricDesign { get => lyricDesign; set => Set(ref lyricDesign, value); }
    public bool LyricClickThrough { get => lyricClickThrough; set => Set(ref lyricClickThrough, value); }
    public bool LyricLocked { get => lyricLocked; set => Set(ref lyricLocked, value); }
    public double LyricOffset { get => lyricOffset; set => Set(ref lyricOffset, value); }
    public string LyricLocal { get => lyricLocal; set => Set(ref lyricLocal, value); }
    public bool LyricDemo { get => lyricDemo; set => Set(ref lyricDemo, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string Account { get => account; set => Set(ref account, value); }
    public string UpdateMessage { get => updateMessage; set => Set(ref updateMessage, value); }

    string room = "";
    string cookie = "";
    int themeMode;
    bool useSystemAccent = true;
    string accentHex = "#6750A4";
    bool clickThrough;
    bool showDanmaku = true;
    bool showLive = true;
    bool showDesktopLyrics;
    bool showAvatar = true;
    string nameColor = "#7C4DFF";
    string textColor = "#1A1A1A";
    string cardColor = "#F5F5F5";
    double corner = 16;
    double fontSize = 18;
    double cardOpacity = 0.86;
    bool adaptiveText;
    bool ttsEnabled;
    double ttsRate = 1;
    string ttsVoice = "";
    string hotkey = "/";
    bool inputLocked;
    bool hideFromTaskbar;
    bool obsEnabled;
    int obsPort = 7788;
    bool showWindowBorder;
    bool showDanmakuMsg = true;
    bool showGift = true;
    bool showSuperchat = true;
    bool showGuard = true;
    bool showEnter;
    bool showFollow = true;
    bool showLike = true;
    bool showShare = true;
    bool danmakuAnimEnabled = true;
    string danmakuAnim = "default";
    double danmakuAnimSpeed = 1;
    double minGiftPrice;
    double ttsVolume = 1;
    string cardFont = "Segoe UI Variable";
    bool experimentalTopmost;
    int danmakuX, danmakuY, danmakuW, danmakuH;
    int liveX, liveY, liveW, liveH;
    int lyricX, lyricY, lyricW, lyricH;
    bool showLiveBorder;
    string lyricFont = "Segoe UI Variable";
    double lyricSize = 22;
    string lyricColor = "#FFFFFF";
    string lyricHighlight = "#FFFFFF";
    string lyricAlign = "center";
    bool lyricShadow = true;
    bool lyricOutline;
    double lyricBg = 0.35;
    // 旧的行数滑杆（1-5）已被单行/双行取代：字段留着做迁移兼容，
    // 渲染不再读它。
    int lyricLines = 1;
    string lyricWeight = "normal";
    string lyricMode = "single";
    string lyricDirection = "horizontal";
    string lyricDesign = "default";
    bool lyricClickThrough = true;
    bool lyricLocked;
    double lyricOffset;
    string lyricLocal = "";
    bool lyricDemo;
    string status = "未连接";
    string account = "未登录";
    string updateMessage = "公开的 GitHub Releases。不需要令牌。";

    public bool Allows(string type) => type switch
    {
        "gift" => ShowGift,
        "superchat" => ShowSuperchat,
        "guard" => ShowGuard,
        "enter" => ShowEnter,
        "follow" => ShowFollow,
        "like" => ShowLike,
        "share" => ShowShare,
        "danmaku" => ShowDanmakuMsg,
        _ => true
    };

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    static AppState Load()
    {
        try
        {
            // 1.3.0 换 WinUI 时把旧 Electron 的设置丢了：这里明确写着
            // “不读那个文件”，所以从 1.2.0 升级上来房间号、Cookie、字体、
            // 字号、颜色、窗口位置全没了。Electron 的 userData 在
            // %APPDATA%\Tokilive\settings.json，第一次启动时按 1.2.0 的
            // 字段名做一次尽力迁移，之后就只读 WinUI 自己的 config.json。
            if (!File.Exists(FilePath))
            {
                var migrated = TryMigrateFromElectron();
                if (migrated != null) { migrated.Save(); return migrated; }
                return new AppState();
            }
            var text = File.ReadAllText(FilePath);
            if (!text.TrimStart().StartsWith('{')) return new AppState();
            return JsonSerializer.Deserialize<AppState>(text, JsonOpts) ?? new AppState();
        }
        catch (Exception ex)
        {
            try
            {
                var note = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tokilive", "logs", "settings.log");
                Directory.CreateDirectory(Path.GetDirectoryName(note)!);
                File.AppendAllText(note, DateTimeOffset.Now + " " + ex.Message + Environment.NewLine);
            }
            catch { /* 配置打不开就用默认值 */ }
            return new AppState();
        }
    }

    static AppState? TryMigrateFromElectron()
    {
        try
        {
            var oldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tokilive", "settings.json");
            if (!File.Exists(oldPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(oldPath));
            var root = doc.RootElement;
            var s = new AppState();
            string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            double Num(string name, double fallback) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
            bool Bool(string name, bool fallback) => root.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : fallback;
            s.Room = Str("roomId");
            s.Cookie = Str("cookie");
            // 1.2.0 是三路字体：fontFamily（拉丁）+ cjkFont + jpFont。
            // WinUI 只有一个 CardFont，中文用户优先用 cjkFont，
            // FontHelper.Resolve 会在后面补上拉丁和系统回退。
            var cjk = Str("cjkFont");
            s.CardFont = cjk.Length > 0 ? cjk : Str("fontFamily");
            s.FontSize = Num("fontSize", s.FontSize);
            s.CardOpacity = Num("opacity", s.CardOpacity);
            s.Corner = Num("radius", s.Corner);
            s.Hotkey = Str("hotkey").Length > 0 ? Str("hotkey") : s.Hotkey;
            s.ObsPort = (int)Num("obsPort", s.ObsPort);
            s.ShowWindowBorder = Bool("showWindowBorder", s.ShowWindowBorder);
            s.ShowLiveBorder = Bool("showLiveBorder", s.ShowLiveBorder);
            s.AdaptiveText = Bool("adaptiveText", s.AdaptiveText);
            s.ExperimentalTopmost = Bool("experimentalGameTopmost", s.ExperimentalTopmost);
            s.HideFromTaskbar = Bool("hideFromTaskbar", s.HideFromTaskbar);
            s.ShowAvatar = Bool("showAvatar", s.ShowAvatar);
            s.MinGiftPrice = Num("minGiftPrice", s.MinGiftPrice);
            s.TtsEnabled = Bool("tts", s.TtsEnabled);
            s.TtsRate = Num("ttsRate", s.TtsRate);
            s.TtsVolume = Num("ttsVolume", s.TtsVolume);
            s.TtsVoice = Str("ttsVoice");
            s.LyricOffset = Num("lyricsOffset", s.LyricOffset);
            s.LyricSize = Num("lyricsSize", s.LyricSize);
            if (root.TryGetProperty("show", out var show))
            {
                bool Show(string n, bool fb) => show.TryGetProperty(n, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : fb;
                s.ShowDanmakuMsg = Show("danmaku", s.ShowDanmakuMsg);
                s.ShowGift = Show("gift", s.ShowGift);
                s.ShowSuperchat = Show("superchat", s.ShowSuperchat);
                s.ShowGuard = Show("guard", s.ShowGuard);
                s.ShowEnter = Show("enter", s.ShowEnter);
            }
            void Bounds(string name, Action<int, int, int, int> set)
            {
                if (!root.TryGetProperty(name, out var b) || b.ValueKind != JsonValueKind.Object) return;
                int I(string n) => b.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
                var (x, y, w, h) = (I("x"), I("y"), I("width"), I("height"));
                if (w > 0 && h > 0) set(x, y, w, h);
            }
            Bounds("bounds", (x, y, w, h) => { s.DanmakuX = x; s.DanmakuY = y; s.DanmakuW = w; s.DanmakuH = h; });
            Bounds("miniBounds", (x, y, w, h) => { s.LiveX = x; s.LiveY = y; s.LiveW = w; s.LiveH = h; });
            return s;
        }
        catch { return null; }
    }

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke();
    }
}
