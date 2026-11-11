using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tokilive;

public sealed class AppState : INotifyPropertyChanged
{
    public const string Version = "1.3.0";
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
    string updateMessage = "公开的 GitHub Releases。1.3.0 不需要令牌。";

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    static AppState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath), JsonOpts) ?? new AppState();
        }
        catch { /* 损坏的本地配置用默认值，不中断启动 */ }
        return new AppState();
    }

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Changed?.Invoke();
    }
}
