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
    bool clickThrough = true;
    bool showDanmaku = true;
    bool showLive = true;
    bool showDesktopLyrics;
    bool showAvatar = true;
    string nameColor = "#7C4DFF";
    string textColor = "#1A1A1A";
    string cardColor = "#F5F5F5";
    double corner = 16;
    double fontSize = 16;
    double cardOpacity = 0.86;
    bool adaptiveText;
    bool ttsEnabled;
    double ttsRate = 1;
    string ttsVoice = "";
    string hotkey = "/";
    bool inputLocked;
    bool hideFromTaskbar;
    bool obsEnabled;
    int obsPort = 9977;
    bool showWindowBorder;
    bool showDanmakuMsg = true;
    bool showGift = true;
    bool showSuperchat = true;
    bool showGuard = true;
    bool showEnter;
    bool showFollow = true;
    bool showLike = true;
    bool showShare = true;
    double ttsVolume = 1;
    string cardFont = "Segoe UI Variable";
    bool experimentalTopmost;
    int danmakuX, danmakuY, danmakuW, danmakuH;
    int liveX, liveY, liveW, liveH;
    int lyricX, lyricY, lyricW, lyricH;
    bool showLiveBorder;
    string lyricFont = "Segoe UI Variable";
    double lyricSize = 28;
    string lyricColor = "#FFFFFF";
    string lyricHighlight = "#FFFFFF";
    string lyricAlign = "center";
    bool lyricShadow = true;
    bool lyricOutline;
    double lyricBg = 0.35;
    int lyricLines = 1;
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
            // 旧 Electron 把 settings.json 放在 Roaming\Tokilive。这里不读那个文件。
            if (!File.Exists(FilePath)) return new AppState();
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

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke();
    }
}
