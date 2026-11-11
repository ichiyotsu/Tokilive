using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tokilive;

public sealed class AppState : INotifyPropertyChanged
{
    public const string Version = "1.4.0";
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
    // OBS 窗口捕获兼容：开 = 走 1.2.0 的做法（去掉 WS_EX_TOOLWINDOW、
    // 加 WS_EX_APPWINDOW），悬浮窗出现在任务栏和 Alt-Tab，OBS 窗口捕获
    // 能在列表里看到它；关（默认）= 保留 WS_EX_TOOLWINDOW，悬浮窗不进
    // 任务栏和 Alt-Tab，程序只留在托盘。
    public bool ObsCaptureCompat { get => obsCaptureCompat; set => Set(ref obsCaptureCompat, value); }
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
    // 1.2.0 补全（46 项审计）：停留时间/条数、字重、阴影、磨砂、卡片主题、
    // 种子色、粉丝牌开关、三路字体、实时窗行为、消息去向、自适应兜底、
    // 设置窗背景/材质/侧栏、粉丝牌样式、输入窗位置、锁定快捷键。
    // 默认值按 1.2.0 main.js DEFAULTS（miniLayout 默认 'h'，miniPills 默认关）。
    public double DanmakuLifetime { get => danmakuLifetime; set => Set(ref danmakuLifetime, value); }
    public int MaxMessages { get => maxMessages; set => Set(ref maxMessages, value); }
    public string DanmakuFontWeight { get => danmakuFontWeight; set => Set(ref danmakuFontWeight, value); }
    public double TextShadow { get => textShadow; set => Set(ref textShadow, value); }
    public double CardBlur { get => cardBlur; set => Set(ref cardBlur, value); }
    public string CardThemeMode { get => cardThemeMode; set => Set(ref cardThemeMode, value); }
    public string SeedColor { get => seedColor; set => Set(ref seedColor, value); }
    public bool ShowMedal { get => showMedal; set => Set(ref showMedal, value); }
    public string CjkFont { get => cjkFont; set => Set(ref cjkFont, value); }
    public string JpFont { get => jpFont; set => Set(ref jpFont, value); }
    public bool MiniStats { get => miniStats; set => Set(ref miniStats, value); }
    public string MiniLayout { get => miniLayout; set => Set(ref miniLayout, value); }
    public bool MiniPills { get => miniPills; set => Set(ref miniPills, value); }
    public double MiniLifetime { get => miniLifetime; set => Set(ref miniLifetime, value); }
    public int MiniMax { get => miniMax; set => Set(ref miniMax, value); }
    public string RouteEnter { get => routeEnter; set => Set(ref routeEnter, value); }
    public string RouteFollow { get => routeFollow; set => Set(ref routeFollow, value); }
    public string RouteShare { get => routeShare; set => Set(ref routeShare, value); }
    public string RouteLike { get => routeLike; set => Set(ref routeLike, value); }
    public string AdaptiveManual { get => adaptiveManual; set => Set(ref adaptiveManual, value); }
    public string AppBackground { get => appBackground; set => Set(ref appBackground, value); }
    public double AppBgOpacity { get => appBgOpacity; set => Set(ref appBgOpacity, value); }
    public string AppMaterial { get => appMaterial; set => Set(ref appMaterial, value); }
    public string SidebarMode { get => sidebarMode; set => Set(ref sidebarMode, value); }
    public string MedalStyle { get => medalStyle; set => Set(ref medalStyle, value); }
    public bool ScBiliColor { get => scBiliColor; set => Set(ref scBiliColor, value); }
    public int InputX { get => inputX; set => Set(ref inputX, value); }
    public int InputY { get => inputY; set => Set(ref inputY, value); }
    public int InputW { get => inputW; set => Set(ref inputW, value); }
    public int InputH { get => inputH; set => Set(ref inputH, value); }
    public string LockHotkey { get => lockHotkey; set => Set(ref lockHotkey, value); }
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
    bool obsCaptureCompat;
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
    double danmakuLifetime = 30;
    int maxMessages = 30;
    string danmakuFontWeight = "700";
    double textShadow = 0.6;
    double cardBlur = 16;
    string cardThemeMode = "system";
    string seedColor = "#6750A4";
    bool showMedal = true;
    string cjkFont = "Source Han Sans SC";
    string jpFont = "Source Han Sans SC";
    bool miniStats = true;
    string miniLayout = "h";
    // 默认开：WinUI 1.3.1 一直显示互动气泡，默认关会让现有用户升级后
    // 气泡突然消失（行为回归）。1.2.0 迁移仍以 Electron 的值（默认关）为准。
    bool miniPills = true;
    double miniLifetime = 6;
    int miniMax = 5;
    string routeEnter = "mini";
    string routeFollow = "mini";
    string routeShare = "mini";
    string routeLike = "mini";
    string adaptiveManual = "light";
    string appBackground = "";
    double appBgOpacity = 0.45;
    string appMaterial = "mica";
    string sidebarMode = "auto";
    string medalStyle = "unified";
    bool scBiliColor;
    int inputX, inputY, inputW, inputH;
    string lockHotkey = "Control+Shift+L";
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
            // 1.2.0 的 hotkey 是锁定快捷键，danmakuHotkey 才是输入快捷键；
            // 以前迁移把锁定快捷键错写进了输入快捷键字段，这里分开。
            s.LockHotkey = Str("hotkey").Length > 0 ? Str("hotkey") : s.LockHotkey;
            var dh = Str("danmakuHotkey");
            if (dh.Length > 0) s.Hotkey = dh;
            s.DanmakuLifetime = Num("lifetime", s.DanmakuLifetime);
            s.MaxMessages = (int)Num("maxMessages", s.MaxMessages);
            s.DanmakuFontWeight = ((int)Num("fontWeight", 700)).ToString();
            s.TextShadow = Num("textShadow", s.TextShadow);
            s.CardBlur = Num("blur", s.CardBlur);
            s.CardThemeMode = Str("themeMode").Length > 0 ? Str("themeMode") : s.CardThemeMode;
            s.SeedColor = Str("seedColor").Length > 0 ? Str("seedColor") : s.SeedColor;
            s.ShowMedal = Bool("showMedal", s.ShowMedal);
            s.CjkFont = Str("cjkFont").Length > 0 ? Str("cjkFont") : s.CjkFont;
            s.JpFont = Str("jpFont").Length > 0 ? Str("jpFont") : s.JpFont;
            s.MiniStats = Bool("miniStats", s.MiniStats);
            s.MiniLayout = Str("miniLayout").Length > 0 ? Str("miniLayout") : s.MiniLayout;
            s.MiniPills = Bool("miniPills", s.MiniPills);
            s.MiniLifetime = Num("miniLifetime", s.MiniLifetime);
            s.MiniMax = (int)Num("miniMax", s.MiniMax);
            s.AdaptiveManual = Str("adaptiveManual").Length > 0 ? Str("adaptiveManual") : s.AdaptiveManual;
            s.AppBackground = Str("appBackground");
            s.AppBgOpacity = Num("appBgOpacity", s.AppBgOpacity);
            s.AppMaterial = Str("appMaterial").Length > 0 ? Str("appMaterial") : s.AppMaterial;
            s.SidebarMode = Str("sidebar").Length > 0 ? Str("sidebar") : s.SidebarMode;
            s.MedalStyle = Str("medalStyle").Length > 0 ? Str("medalStyle") : s.MedalStyle;
            s.ScBiliColor = Bool("scBiliColor", s.ScBiliColor);
            if (root.TryGetProperty("route", out var route))
            {
                string R(string n, string fb) => route.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fb : fb;
                s.RouteEnter = R("enter", s.RouteEnter);
                s.RouteFollow = R("follow", s.RouteFollow);
                s.RouteShare = R("share", s.RouteShare);
                s.RouteLike = R("like", s.RouteLike);
            }
            s.ObsPort = (int)Num("obsPort", s.ObsPort);
            s.ShowWindowBorder = Bool("showWindowBorder", s.ShowWindowBorder);
            s.ShowLiveBorder = Bool("showLiveBorder", s.ShowLiveBorder);
            s.AdaptiveText = Bool("adaptiveText", s.AdaptiveText);
            s.ExperimentalTopmost = Bool("experimentalGameTopmost", s.ExperimentalTopmost);
            s.HideFromTaskbar = Bool("hideFromTaskbar", s.HideFromTaskbar);
            // 1.2.0 没有这个设置，行为无条件是开；现在默认关，用户要
            // OBS 窗口捕获兼容再自己开。
            s.ObsCaptureCompat = Bool("obsCaptureCompat", s.ObsCaptureCompat);
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
