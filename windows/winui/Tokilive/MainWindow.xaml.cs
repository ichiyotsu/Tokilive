using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Tokilive;

public sealed record CardTheme(string NameColor, string TextColor, string CardColor, string CardFont, double FontSize, double Corner, double CardOpacity, bool ShowAvatar);

public sealed partial class MainWindow : Window
{
    readonly OverlayWindow _danmaku;
    readonly OverlayWindow _live;
    readonly OverlayWindow _lyrics;
    readonly InputWindow _input;
    bool _loading = true;
    bool _tray;
    bool _exiting;
    bool _lastObsEnabled;
    int _lastObsPort = -1;
    string _lastHotkey = "";
    string _lastLockHotkey = "";
    string _lastMaterial = "";
    string _lastBgPath = "\0";
    double _lastBgOpacity = -1;
    bool _dragSubscribed;
    DispatcherTimer? _refreshTimer;
    DispatcherTimer? _z;
    DispatcherTimer? _sample;
    DispatcherTimer? _lyricTimer;
    DispatcherTimer? _expire;
    bool _updateArmed;
    // 同步颜色选择器时置位，避免 ColorChanged -> 写文本框 -> Pull -> 再同步的循环。
    bool _syncingColor;
    bool _updatingSeek;
    string _coverKey = "";
    // 全部歌词页的重绘节流：歌没换、当前行没变时不重建列表，
    // 否则 500ms 的歌词定时器每次都会把滚动位置弹回顶部。
    string _fullLyricsKey = "";
    int _fullLyricsIndex = -1;
    DispatcherTimer? _seekTimer;

    public MainWindow()
    {
        InitializeComponent();
        _danmaku = new OverlayWindow(OverlayWindow.Kind.Danmaku);
        _live = new OverlayWindow(OverlayWindow.Kind.Live);
        _lyrics = new OverlayWindow(OverlayWindow.Kind.Lyrics);
        _input = new InputWindow();
        NativeWin.ApplyMain(this);
        TryBackdrop();
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1100, 760));
        NativeWin.UpdateMainDragRectangles(this);
        // 拖动区要跟着窗口大小走：最大化/还原/改大小都会触发 SizeChanged。
        SizeChanged += (_, _) => NativeWin.UpdateMainDragRectangles(this);
        // 构造时窗口还没显示，XamlRoot 可能拿不到，缩放会按 1.0 算；
        // 激活后 XamlRoot 就有了，再按真实缩放重算一次，避免高 DPI 下
        // 拖动区盖住右边的最小化/最大化/关闭按钮。
        Activated += (_, _) =>
        {
            NativeWin.UpdateMainDragRectangles(this);
            // XamlRoot.Changed 在改 DPI/换显示器时触发，拖动区跟着实时缩放重算；
            // 只靠 Activated 的话，窗口不失活地换屏时拖动区会一直过期，盖住最小化按钮。
            if (!_dragSubscribed && Content?.XamlRoot is { } xr)
            {
                _dragSubscribed = true;
                xr.Changed += (_, _) => NativeWin.UpdateMainDragRectangles(this);
            }
        };
        Closed += (_, args) =>
        {
            // #30 关到托盘：1.2.0 的 window-all-closed 行为——主窗口关掉只是
            // 藏起来（悬浮窗和连接都继续跑），点托盘或托盘菜单“设置”再回来。
            // 只有托盘菜单“退出”走真的退出（_exiting）。托盘没建起来时
            // 藏了就找不回来，所以那时不拦，直接走真的退出。
            if (!_exiting && _tray)
            {
                args.Handled = true;
                AppWindow.Hide();
                return;
            }
            // 真退出：先停掉所有定时器，免得窗口拆到一半时 tick 又跑进来
            // 取已经没了的句柄。
            _z?.Stop();
            _sample?.Stop();
            _lyricTimer?.Stop();
            _expire?.Stop();
            _refreshTimer?.Stop();
            _seekTimer?.Stop();
            Hotkey.Unbind();
            if (_tray) NativeWin.RemoveTray(NativeWin.Hwnd(this));
            _danmaku.Close();
            _live.Close();
            _lyrics.Close();
            _input.Close();
            Application.Current.Exit();
        };
        LoadForm();
        _loading = false;
        Nav.SelectedItem = Nav.MenuItems[0];
        // #26 防抖：弹幕洪水时 Lines/Pills 每来一条就 CollectionChanged，
        // 原来每次都全量重建卡片；现在 100ms 内只刷最后一次。
        // 1 秒过期定时器和 ShowPlayer 还是直接刷，不走防抖。
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); RefreshCards(); };
        LiveService.Lines.CollectionChanged += (_, _) => ScheduleRefresh();
        LiveService.Pills.CollectionChanged += (_, _) => ScheduleRefresh();
        LiveService.StatsChanged += () => ScheduleRefresh();
        // #35 下载进度：UpdateService.Progress 在后台线程上发，App.UI 切回 UI 线程。
        UpdateService.Progress += (received, total) => App.UI(() =>
        {
            if (UpdateProgress == null) return;
            UpdateProgress.Visibility = Visibility.Visible;
            UpdateProgressText.Visibility = Visibility.Visible;
            if (total > 0)
            {
                UpdateProgress.IsIndeterminate = false;
                UpdateProgress.Value = Math.Clamp(received * 100.0 / total, 0, 100);
                UpdateProgressText.Text = $"下载中 {received * 100 / total}%（{received / 1024} KB / {total / 1024} KB）";
            }
            else
            {
                UpdateProgress.IsIndeterminate = true;
                UpdateProgressText.Text = $"下载中…（{received / 1024} KB）";
            }
        });
        // SMTC 事件（切歌/暂停/拖进度）触发的 Refresh 会发 Updated：
        // 事件在后台线程上，App.UI 切回 UI 线程再刷新播放器栏和歌词窗。
        LyricsService.Updated += () => App.UI(() => { ShowPlayer(); _lyrics.Render(); });
        _ = LiveService.RefreshAccount().ContinueWith(_ => App.UI(() => { if (AccountText != null) AccountText.Text = AppState.Current.Account; }));
        var hwnd = NativeWin.Hwnd(this);
        _tray = NativeWin.TryTray(hwnd, () => { Activate(); AppWindow.Show(); });
        if (!_tray) AppState.Current.HideFromTaskbar = false;
        ApplyTaskbar();
        Hotkey.Bind(hwnd, () => _input.Toggle());
        WireTray();
        _z = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _z.Tick += (_, _) => KeepOrder();
        _z.Start();
        _sample = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sample.Tick += (_, _) => Sample();
        _sample.Start();
        // 歌词定时器从 1 秒改成 500ms：行切换更顺滑。切歌/暂停这些
        // 大变化走 SMTC 事件立刻刷新，定时器只是兜底。
        _lyricTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _lyricTimer.Tick += async (_, _) => { try { await LyricsService.Refresh(); ShowPlayer(); _lyrics.Render(); } catch { } };
        _lyricTimer.Start();
        // 弹幕 30 秒、实时窗互动 6 秒后过期，但没有新消息就不会有人触发
        // 重绘，所以每秒只在确实有东西过期时才重绘一次。
        _expire = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _expire.Tick += (_, _) => { if (LiveService.HasExpired()) RefreshCards(); };
        _expire.Start();
        _seekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _seekTimer.Tick += async (_, _) =>
        {
            _seekTimer.Stop();
            try { await LyricsService.Seek(PlayerSeek.Value); } catch { /* 播放器不接受拖动 */ }
        };
        ApplyVisibility();
        RefreshCards();
        ObsServer.Apply();
    }

    void ScheduleRefresh()
    {
        // _refreshTimer 在构造里建好；LoadForm/RefreshCards 期间还不会有人调它。
        if (_refreshTimer == null) { App.UI(RefreshCards); return; }
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    // #29/#32 托盘菜单接线：NativeWin 只发事件，真正的动作都在这里。
    // 注意弹幕窗和实时窗的“锁定”共用同一个 ClickThrough（AppState 里
    // 没有分开的 DanmakuLocked/LiveLocked 字段），所以托盘里“锁定弹幕窗”
    // 和“锁定实时窗”改的是同一个开关；歌词窗另有自己的 LyricLocked。
    // 锁全部 = 弹幕/实时穿透 + 歌词锁定一起开；全锁了再按一次就是全解锁。
    void WireTray()
    {
        NativeWin.TrayShowSettings += () => App.UI(() => { AppWindow.Show(); Activate(); });
        NativeWin.TrayToggleLockAll += () => App.UI(ToggleLockAll);
        NativeWin.TrayToggleDanmakuLock += () => App.UI(() => ClickSwitch.IsOn = !ClickSwitch.IsOn);
        NativeWin.TrayToggleLiveLock += () => App.UI(() => ClickSwitch.IsOn = !ClickSwitch.IsOn);
        NativeWin.TrayToggleDanmakuVisible += () => App.UI(() => DanmakuWindowSwitch.IsOn = !DanmakuWindowSwitch.IsOn);
        NativeWin.TrayToggleLiveVisible += () => App.UI(() => LiveSwitch.IsOn = !LiveSwitch.IsOn);
        NativeWin.TrayReconnect += () => App.UI(() => { AppState.Current.Room = RoomBox.Text; LiveService.Connect(RoomBox.Text); });
        NativeWin.TrayExit += () => App.UI(() => { _exiting = true; Close(); });
        NativeWin.TrayStatusText = () => AppState.Current.Status;
        NativeWin.TrayAllLocked = () => AppState.Current.ClickThrough && AppState.Current.LyricLocked;
        NativeWin.TrayDanmakuLocked = () => AppState.Current.ClickThrough;
        NativeWin.TrayLiveLocked = () => AppState.Current.ClickThrough;
        NativeWin.TrayDanmakuVisible = () => AppState.Current.ShowDanmaku;
        NativeWin.TrayLiveVisible = () => AppState.Current.ShowLive;
        Hotkey.LockHotkeyPressed += () => App.UI(ToggleLockAll);
    }

    void ToggleLockAll()
    {
        var allLocked = AppState.Current.ClickThrough && AppState.Current.LyricLocked;
        // 走开关本身：Toggled 会触发 Pull，保存 + 刷新悬浮窗 chrome，
        // 不用在这里再写一遍 AppState 和 Save。
        ClickSwitch.IsOn = !allLocked;
        LyricLockSwitch.IsOn = !allLocked;
    }

    // 顶栏拖动的兜底：拖动区（SetDragRectangles）正常时，点在标题栏上
    // 的操作会被系统当成标题栏拖动，不会走到这里；只有拖动区设不上
    // 时，这里才接住按下事件，用 WM_NCLBUTTONDOWN 模拟按住标题栏拖动，
    // 和悬浮窗的拖动是同一套办法。窗口就不会出现拖不动的情况。
    void TitleBarRow_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        NativeWin.DragWindow(NativeWin.Hwnd(this));
    }

    void TryBackdrop()
    {
        // AppMaterial：mica / acrylic / tabbed（Mica Alt）/ none（纯色）。
        // 不支持的组合退回下一档，最后纯色；MaterialText 告诉用户实际用的什么。
        try
        {
            var material = AppState.Current.AppMaterial;
            if (material == "none")
            {
                SystemBackdrop = null;
                MaterialText.Text = "设置窗口使用纯色（材质已关）。";
            }
            else if (material == "acrylic" && DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
                MaterialText.Text = "这个窗口使用 Acrylic。";
            }
            else if (material == "tabbed" && MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                MaterialText.Text = "这个窗口使用 Mica Alt（标签页风格）。";
            }
            else if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop();
                MaterialText.Text = "这个窗口使用 Mica。Windows 10 没有 Mica 时会退回纯色。";
            }
            else if (DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
                MaterialText.Text = "这台系统没有 Mica，设置窗口使用 Acrylic。";
            }
            else MaterialText.Text = "这台系统没有 Mica 或 Acrylic，设置窗口使用纯色。";
        }
        catch { MaterialText.Text = "材质不可用，设置窗口使用纯色。"; }
    }

    void LoadForm()
    {
        var s = AppState.Current;
        RoomBox.Text = s.Room;
        AccountText.Text = s.Account;
        StatusText.Text = s.Status;
        CookieBox.Text = s.Cookie;
        LockHotkeyBox.Text = s.LockHotkey;
        _lastLockHotkey = s.LockHotkey;
        AvatarSwitch.IsOn = s.ShowAvatar;
        NameColorBox.Text = s.NameColor;
        CardColorBox.Text = s.CardColor;
        TextColorBox.Text = s.TextColor;
        FillFontCombo(CardFontBox);
        SelectFont(CardFontBox, s.CardFont);
        FillFontCombo(LyricFontBox);
        SelectFont(LyricFontBox, s.LyricFont);
        FillFontCombo(CjkFontBox);
        SelectFont(CjkFontBox, s.CjkFont);
        FillFontCombo(JpFontBox);
        SelectFont(JpFontBox, s.JpFont);
        FontSlider.Value = s.FontSize;
        AdaptiveSwitch.IsOn = s.AdaptiveText;
        CornerSlider.Value = s.Corner;
        OpacitySlider.Value = s.CardOpacity;
        BorderSwitch.IsOn = s.ShowWindowBorder;
        ClickSwitch.IsOn = s.ClickThrough;
        MsgDanmaku.IsOn = s.ShowDanmakuMsg;
        MsgGift.IsOn = s.ShowGift;
        MsgSc.IsOn = s.ShowSuperchat;
        MsgGuard.IsOn = s.ShowGuard;
        MsgEnter.IsOn = s.ShowEnter;
        MsgFollow.IsOn = s.ShowFollow;
        MsgLike.IsOn = s.ShowLike;
        MsgShare.IsOn = s.ShowShare;
        AnimSwitch.IsOn = s.DanmakuAnimEnabled;
        SelectCombo(AnimBox, s.DanmakuAnim);
        AnimSpeedSlider.Value = s.DanmakuAnimSpeed;
        LifetimeBox.Value = s.DanmakuLifetime;
        MaxMessagesBox.Value = s.MaxMessages;
        SelectCombo(FontWeightBox, s.DanmakuFontWeight);
        TextShadowSlider.Value = s.TextShadow;
        CardBlurSwitch.IsOn = s.CardBlur > 0;
        SelectCombo(CardThemeBox, s.CardThemeMode);
        SeedColorBox.Text = s.SeedColor;
        ShowMedalSwitch.IsOn = s.ShowMedal;
        SelectFont(CjkFontBox, s.CjkFont);
        SelectFont(JpFontBox, s.JpFont);
        SelectCombo(MedalStyleBox, s.MedalStyle);
        ScBiliColorSwitch.IsOn = s.ScBiliColor;
        MinGiftPriceBox.Value = s.MinGiftPrice;
        HotkeyBox.Text = s.Hotkey;
        TtsSwitch.IsOn = s.TtsEnabled;
        RateSlider.Value = s.TtsRate;
        VolumeSlider.Value = s.TtsVolume;
        foreach (var voice in SpeechService.Voices()) VoiceBox.Items.Add(voice);
        if (VoiceBox.Items.Count == 0) VoiceBox.Items.Add("系统默认");
        // 1.3.1 的 bug：这里永远 SelectedIndex = 0，保存的 TTS 语音重启后丢失。
        var savedVoice = VoiceBox.Items.IndexOf(s.TtsVoice);
        VoiceBox.SelectedIndex = savedVoice >= 0 ? savedVoice : 0;
        LiveSwitch.IsOn = s.ShowLive;
        MiniStatsSwitch.IsOn = s.MiniStats;
        SelectCombo(MiniLayoutBox, s.MiniLayout);
        MiniPillsSwitch.IsOn = s.MiniPills;
        MiniLifetimeBox.Value = s.MiniLifetime;
        MiniMaxBox.Value = s.MiniMax;
        SelectCombo(RouteEnterBox, s.RouteEnter);
        SelectCombo(RouteFollowBox, s.RouteFollow);
        SelectCombo(RouteShareBox, s.RouteShare);
        SelectCombo(RouteLikeBox, s.RouteLike);
        DesktopLyricsSwitch.IsOn = s.ShowDesktopLyrics;
        LyricDemoSwitch.IsOn = s.LyricDemo;
        // 设计下拉框从 LyricsDesigns.All 填，改表就自动多一项。
        // LoadForm 时 _loading 还是 true，SelectionChanged 里的设计应用
        // 会直接返回，不会把保存过的单项改动盖掉。
        foreach (var design in LyricsDesigns.All) LyricDesignBox.Items.Add(new ComboBoxItem { Content = design.Name, Tag = design.Id });
        SelectCombo(LyricDesignBox, s.LyricDesign);
        SelectFont(LyricFontBox, s.LyricFont);
        LyricSizeSlider.Value = s.LyricSize;
        SelectCombo(LyricWeightBox, s.LyricWeight);
        LyricColorBox.Text = s.LyricColor;
        LyricHiBox.Text = s.LyricHighlight;
        LyricAlignBox.SelectedIndex = s.LyricAlign == "left" ? 0 : s.LyricAlign == "right" ? 2 : 1;
        LyricShadowSwitch.IsOn = s.LyricShadow;
        LyricOutlineSwitch.IsOn = s.LyricOutline;
        LyricBgSlider.Value = s.LyricBg;
        SelectCombo(LyricModeBox, s.LyricMode);
        SelectCombo(LyricDirectionBox, s.LyricDirection);
        LyricClickSwitch.IsOn = s.LyricClickThrough;
        LyricWordSwitch.IsOn = s.LyricWordByWord;
        LyricLockSwitch.IsOn = s.LyricLocked;
        LyricOffsetSlider.Value = s.LyricOffset;
        DanmakuWindowSwitch.IsOn = s.ShowDanmaku;
        MainTopmostSwitch.IsOn = s.MainTopmost;
        OverlayTopmostSwitch.IsOn = s.OverlayTopmost;
        TaskbarSwitch.IsOn = s.HideFromTaskbar;
        ObsSwitch.IsOn = s.ObsEnabled;
        ObsCaptureSwitch.IsOn = s.ObsCaptureCompat;
        PortBox.Value = s.ObsPort;
        ThemeBox.SelectedIndex = s.ThemeMode;
        AccentSwitch.IsOn = s.UseSystemAccent;
        AccentBox.Text = s.AccentHex;
        SelectCombo(AdaptiveManualBox, s.AdaptiveManual);
        SelectCombo(AppMaterialBox, s.AppMaterial);
        _lastMaterial = s.AppMaterial;
        AppBgOpacitySlider.Value = s.AppBgOpacity;
        SelectCombo(SidebarModeBox, s.SidebarMode);
        SplashSwitch.IsOn = s.ShowSplash;
        UpdateText.Text = s.UpdateMessage;
        VersionText.Text = AppState.Version;
        UpdateProgress.Visibility = Visibility.Collapsed;
        UpdateProgressText.Visibility = Visibility.Collapsed;
        UpdateNotesExpander.Visibility = Visibility.Collapsed;
        Wire();
        SyncColorPickers();
        ApplyTheme();
        ApplySidebar();
        ApplyBackground();
    }

    // 接受 #RRGGBB，或不带 # 的 6 位 hex，统一成大写的 #RRGGBB。
    // 打错了（长度不对、不是 hex 字符）返回 false，Pull() 会保留
    // 原来的 AppState 值，而不是把错文本存进去；以前错文本是直接
    // 存了，OverlayWindow.Parse 再悄悄回退到 #1A1A1A。
    static bool TryNormalizeHex(string? text, out string hex)
    {
        hex = "";
        var t = (text ?? "").Trim().TrimStart('#');
        if (t.Length != 6) return false;
        if (!int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out _)) return false;
        hex = "#" + t.ToUpperInvariant();
        return true;
    }

    void SyncColorPickers()
    {
        _syncingColor = true;
        try
        {
            SyncOneColor(NameColorPicker, NameColorBox, NameColorSwatch);
            SyncOneColor(CardColorPicker, CardColorBox, CardColorSwatch);
            SyncOneColor(TextColorPicker, TextColorBox, TextColorSwatch);
            SyncOneColor(LyricColorPicker, LyricColorBox, LyricColorSwatch);
            SyncOneColor(LyricHiPicker, LyricHiBox, LyricHiSwatch);
            SyncOneColor(AccentPicker, AccentBox, AccentSwatch);
            SyncOneColor(SeedColorPicker, SeedColorBox, SeedColorSwatch);
        }
        finally { _syncingColor = false; }
    }

    static void SyncOneColor(ColorPicker picker, TextBox box, Border swatch)
    {
        // 用现成的 OverlayWindow.Parse 把 hex 转成 Color；hex 非法时
        // Parse 会回退到 #1A1A1A，所以先用 TryNormalizeHex 校验，
        // 非法就让选择器和色块保持原样，不拿回退色去骗人。
        if (!TryNormalizeHex(box.Text, out var hex)) return;
        var color = OverlayWindow.Parse(hex);
        picker.Color = color;
        swatch.Background = new SolidColorBrush(color);
    }

    void ColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_syncingColor) return;
        if (sender.Tag is not string boxName) return;
        if (Root.FindName(boxName) is not TextBox box) return;
        var color = sender.Color;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        // 色块名就是文本框名把 Box 换成 Swatch，例如 TextColorBox -> TextColorSwatch。
        if (Root.FindName(boxName.Replace("Box", "Swatch")) is Border swatch)
            swatch.Background = new SolidColorBrush(color);
        // 写回文本框后走现成的 TextChanged -> Pull() 保存并刷新悬浮窗。
        box.Text = hex;
    }

    void TextColorChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button chip && chip.Tag is string hex) TextColorBox.Text = hex;
    }

    void Wire()
    {
        void Save(object? _, object? __) { if (!_loading) Pull(); }
        foreach (var box in new TextBox[] { RoomBox, NameColorBox, CardColorBox, TextColorBox, HotkeyBox, LyricColorBox, LyricHiBox, AccentBox, SeedColorBox, LockHotkeyBox })
            box.TextChanged += (_, _) => Save(null, null);
        foreach (var box in new ComboBox[] { CardFontBox, LyricFontBox, CjkFontBox, JpFontBox })
            box.SelectionChanged += (_, _) => Save(null, null);
        foreach (var sw in new ToggleSwitch[] { AvatarSwitch, AdaptiveSwitch, BorderSwitch, ClickSwitch, CardBlurSwitch, MsgDanmaku, MsgGift, MsgSc, MsgGuard, MsgEnter, MsgFollow, MsgLike, MsgShare, AnimSwitch, TtsSwitch, LiveSwitch, DesktopLyricsSwitch, LyricDemoSwitch, LyricShadowSwitch, LyricOutlineSwitch, LyricClickSwitch, LyricLockSwitch, LyricWordSwitch, DanmakuWindowSwitch, MainTopmostSwitch, OverlayTopmostSwitch, TaskbarSwitch, ObsSwitch, ObsCaptureSwitch, AccentSwitch, ShowMedalSwitch, ScBiliColorSwitch, MiniStatsSwitch, MiniPillsSwitch })
            sw.Toggled += (_, _) => Save(null, null);
        foreach (var slider in new Slider[] { FontSlider, CornerSlider, OpacitySlider, RateSlider, VolumeSlider, LyricSizeSlider, LyricBgSlider, LyricOffsetSlider, AnimSpeedSlider, TextShadowSlider, AppBgOpacitySlider })
            slider.ValueChanged += (_, _) => Save(null, null);
        ThemeBox.SelectionChanged += (_, _) => Save(null, null);
        AnimBox.SelectionChanged += (_, _) => Save(null, null);
        FontWeightBox.SelectionChanged += (_, _) => Save(null, null);
        CardThemeBox.SelectionChanged += (_, _) => Save(null, null);
        MedalStyleBox.SelectionChanged += (_, _) => Save(null, null);
        MiniLayoutBox.SelectionChanged += (_, _) => Save(null, null);
        RouteEnterBox.SelectionChanged += (_, _) => Save(null, null);
        RouteFollowBox.SelectionChanged += (_, _) => Save(null, null);
        RouteShareBox.SelectionChanged += (_, _) => Save(null, null);
        RouteLikeBox.SelectionChanged += (_, _) => Save(null, null);
        AdaptiveManualBox.SelectionChanged += (_, _) => Save(null, null);
        AppMaterialBox.SelectionChanged += (_, _) => Save(null, null);
        SidebarModeBox.SelectionChanged += (_, _) => Save(null, null);
        LyricAlignBox.SelectionChanged += (_, _) => Save(null, null);
        LyricWeightBox.SelectionChanged += (_, _) => Save(null, null);
        LyricModeBox.SelectionChanged += (_, _) => Save(null, null);
        LyricDirectionBox.SelectionChanged += (_, _) => Save(null, null);
        VoiceBox.SelectionChanged += (_, _) => Save(null, null);
        PortBox.ValueChanged += (_, _) => Save(null, null);
        LifetimeBox.ValueChanged += (_, _) => Save(null, null);
        MaxMessagesBox.ValueChanged += (_, _) => Save(null, null);
        MinGiftPriceBox.ValueChanged += (_, _) => Save(null, null);
        MiniLifetimeBox.ValueChanged += (_, _) => Save(null, null);
        MiniMaxBox.ValueChanged += (_, _) => Save(null, null);
        TtsSwitch.Toggled += (_, _) => { if (!TtsSwitch.IsOn) SpeechService.Stop(); };
        SplashSwitch.Toggled += (_, _) => Save(null, null);
    }

    void Pull()
    {
        var s = AppState.Current;
        var wasTts = s.TtsEnabled;
        s.Room = RoomBox.Text;
        s.ShowAvatar = AvatarSwitch.IsOn;
        s.ShowSplash = SplashSwitch.IsOn;
        // 颜色只接受合法的 #RRGGBB（或不带 # 的 6 位 hex，自动补 # 并大写）；
        // 非法文本保留原来的 AppState 值，不存错颜色。
        if (TryNormalizeHex(NameColorBox.Text, out var nameHex)) { s.NameColor = nameHex; if (NameColorBox.Text != nameHex) NameColorBox.Text = nameHex; }
        if (TryNormalizeHex(CardColorBox.Text, out var cardHex)) { s.CardColor = cardHex; if (CardColorBox.Text != cardHex) CardColorBox.Text = cardHex; }
        if (TryNormalizeHex(TextColorBox.Text, out var textHex)) { s.TextColor = textHex; if (TextColorBox.Text != textHex) TextColorBox.Text = textHex; }
        s.CardFont = CardFontBox.SelectedItem as string ?? s.CardFont;
        s.FontSize = FontSlider.Value;
        s.AdaptiveText = AdaptiveSwitch.IsOn;
        s.Corner = CornerSlider.Value;
        s.CardOpacity = OpacitySlider.Value;
        s.ShowWindowBorder = BorderSwitch.IsOn;
        s.ClickThrough = ClickSwitch.IsOn;
        s.ShowDanmakuMsg = MsgDanmaku.IsOn;
        s.ShowGift = MsgGift.IsOn;
        s.ShowSuperchat = MsgSc.IsOn;
        s.ShowGuard = MsgGuard.IsOn;
        s.ShowEnter = MsgEnter.IsOn;
        s.ShowFollow = MsgFollow.IsOn;
        s.ShowLike = MsgLike.IsOn;
        s.ShowShare = MsgShare.IsOn;
        s.DanmakuAnimEnabled = AnimSwitch.IsOn;
        s.DanmakuAnim = ComboValue(AnimBox) ?? s.DanmakuAnim;
        s.DanmakuAnimSpeed = AnimSpeedSlider.Value;
        s.DanmakuLifetime = LifetimeBox.Value;
        s.MaxMessages = (int)MaxMessagesBox.Value;
        s.DanmakuFontWeight = ComboValue(FontWeightBox) ?? s.DanmakuFontWeight;
        s.TextShadow = TextShadowSlider.Value;
        s.CardBlur = CardBlurSwitch.IsOn ? 16 : 0;
        s.CardThemeMode = ComboValue(CardThemeBox) ?? s.CardThemeMode;
        if (TryNormalizeHex(SeedColorBox.Text, out var seedHex)) { s.SeedColor = seedHex; if (SeedColorBox.Text != seedHex) SeedColorBox.Text = seedHex; }
        s.ShowMedal = ShowMedalSwitch.IsOn;
        s.CjkFont = CjkFontBox.SelectedItem as string ?? s.CjkFont;
        s.JpFont = JpFontBox.SelectedItem as string ?? s.JpFont;
        s.MedalStyle = ComboValue(MedalStyleBox) ?? s.MedalStyle;
        s.ScBiliColor = ScBiliColorSwitch.IsOn;
        s.MinGiftPrice = MinGiftPriceBox.Value;
        s.Hotkey = HotkeyBox.Text;
        s.TtsEnabled = TtsSwitch.IsOn;
        s.TtsRate = RateSlider.Value;
        s.TtsVolume = VolumeSlider.Value;
        s.TtsVoice = VoiceBox.SelectedItem as string ?? "";
        s.ShowLive = LiveSwitch.IsOn;
        s.MiniStats = MiniStatsSwitch.IsOn;
        s.MiniLayout = ComboValue(MiniLayoutBox) ?? s.MiniLayout;
        s.MiniPills = MiniPillsSwitch.IsOn;
        s.MiniLifetime = MiniLifetimeBox.Value;
        s.MiniMax = (int)MiniMaxBox.Value;
        s.RouteEnter = ComboValue(RouteEnterBox) ?? s.RouteEnter;
        s.RouteFollow = ComboValue(RouteFollowBox) ?? s.RouteFollow;
        s.RouteShare = ComboValue(RouteShareBox) ?? s.RouteShare;
        s.RouteLike = ComboValue(RouteLikeBox) ?? s.RouteLike;
        s.ShowDesktopLyrics = DesktopLyricsSwitch.IsOn;
        s.LyricDemo = LyricDemoSwitch.IsOn;
        s.LyricFont = LyricFontBox.SelectedItem as string ?? s.LyricFont;
        s.LyricSize = LyricSizeSlider.Value;
        s.LyricWeight = ComboValue(LyricWeightBox) ?? s.LyricWeight;
        if (TryNormalizeHex(LyricColorBox.Text, out var lyricHex)) { s.LyricColor = lyricHex; if (LyricColorBox.Text != lyricHex) LyricColorBox.Text = lyricHex; }
        if (TryNormalizeHex(LyricHiBox.Text, out var lyricHiHex)) { s.LyricHighlight = lyricHiHex; if (LyricHiBox.Text != lyricHiHex) LyricHiBox.Text = lyricHiHex; }
        s.LyricAlign = (LyricAlignBox.SelectedItem as ComboBoxItem)?.Content as string ?? "center";
        s.LyricShadow = LyricShadowSwitch.IsOn;
        s.LyricOutline = LyricOutlineSwitch.IsOn;
        s.LyricBg = LyricBgSlider.Value;
        s.LyricMode = ComboValue(LyricModeBox) ?? s.LyricMode;
        s.LyricDirection = ComboValue(LyricDirectionBox) ?? s.LyricDirection;
        s.LyricDesign = ComboValue(LyricDesignBox) ?? s.LyricDesign;
        s.LyricClickThrough = LyricClickSwitch.IsOn;
        s.LyricWordByWord = LyricWordSwitch.IsOn;
        s.LyricLocked = LyricLockSwitch.IsOn;
        s.LyricOffset = LyricOffsetSlider.Value;
        s.ShowDanmaku = DanmakuWindowSwitch.IsOn;
        s.MainTopmost = MainTopmostSwitch.IsOn;
        s.OverlayTopmost = OverlayTopmostSwitch.IsOn;
        s.HideFromTaskbar = TaskbarSwitch.IsOn && _tray;
        s.ObsEnabled = ObsSwitch.IsOn;
        s.ObsCaptureCompat = ObsCaptureSwitch.IsOn;
        s.ObsPort = (int)PortBox.Value;
        s.ThemeMode = ThemeBox.SelectedIndex < 0 ? 0 : ThemeBox.SelectedIndex;
        s.UseSystemAccent = AccentSwitch.IsOn;
        if (TryNormalizeHex(AccentBox.Text, out var accentHex)) { s.AccentHex = accentHex; if (AccentBox.Text != accentHex) AccentBox.Text = accentHex; }
        s.AdaptiveManual = ComboValue(AdaptiveManualBox) ?? s.AdaptiveManual;
        s.AppMaterial = ComboValue(AppMaterialBox) ?? s.AppMaterial;
        s.AppBgOpacity = AppBgOpacitySlider.Value;
        s.SidebarMode = ComboValue(SidebarModeBox) ?? s.SidebarMode;
        if (wasTts && !s.TtsEnabled) SpeechService.Stop();
        s.Save();
        ApplyTheme();
        ApplyVisibility();
        ApplyTaskbar();
        // 1.3.1 的 bug：每敲一个字、每拖一下滑杆，Pull() 都会重启 OBS 服务
        // （先 Stop 再 Start，浏览器源断连）并重绑全局热键。只在相关值
        // 真变了的时候才做，字体/颜色这些改动不再惊动它们。
        if (s.ObsEnabled != _lastObsEnabled || s.ObsPort != _lastObsPort) { ObsServer.Apply(); _lastObsEnabled = s.ObsEnabled; _lastObsPort = s.ObsPort; }
        if (s.Hotkey != _lastHotkey) { Hotkey.Bind(NativeWin.Hwnd(this), () => _input.Toggle()); _lastHotkey = s.Hotkey; }
        // 锁定快捷键：只有文本真变了才重注册。Reapply 会校验格式
        //（空串、无修饰键、注册失败都返回 false），校验不过就把
        // 文本框弹回旧值、把旧热键重新注册上，不把错的存进去。
        var lockText = LockHotkeyBox.Text.Trim();
        if (lockText != _lastLockHotkey)
        {
            // 留空 = 不注册锁定热键：Reapply("") 会先注销旧的再返回 false，
            // 空串本身是合法选择，直接存下来。
            if (lockText.Length == 0) { Hotkey.Reapply(""); s.LockHotkey = ""; _lastLockHotkey = ""; s.Save(); }
            else if (Hotkey.Reapply(lockText)) { s.LockHotkey = lockText; _lastLockHotkey = lockText; s.Save(); }
            else { LockHotkeyBox.Text = s.LockHotkey; Hotkey.Reapply(); }
        }
        if (s.AppMaterial != _lastMaterial) { TryBackdrop(); _lastMaterial = s.AppMaterial; }
        ApplySidebar();
        ApplyBackground();
        // 手打 hex 合法时也把选择器和色块同步过去；非法时 SyncOneColor
        // 会跳过，选择器保持原样。_syncingColor 保证这里不会循环触发 Pull。
        SyncColorPickers();
        RefreshCards();
        OverlayUrl.Text = ObsServer.OverlayUrl;
        MiniUrl.Text = ObsServer.MiniUrl;
        if (!_tray) TaskbarSwitch.IsOn = false;
    }

    void ApplyTheme()
    {
        Root.RequestedTheme = AppState.Current.ThemeMode switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        // #14 强调色跟随 UI：UseSystemAccent 关掉时，把自定义 AccentHex
        // 写进应用级资源。NavigationView 的选中指示条、开关、按钮这些
        // 强调色控件读的都是 AccentFillColor*Brush / SystemAccentColor，
        // 这里两边都盖掉；打开“使用 Windows 强调色”时把覆盖删掉、恢复系统色。
        // 注意：WinUI 的 ThemeResource 在主题切换/窗口重建时可能重新解析，
        // 这层覆盖在真机上到底能盖住多少控件，必须在 Windows 上看；
        // 盖不住时设置页就还是系统强调色，不会因此报错或崩掉。
        try
        {
            var resources = Application.Current.Resources;
            if (AppState.Current.UseSystemAccent)
            {
                resources.Remove("SystemAccentColor");
                resources.Remove("AccentFillColorDefaultBrush");
                resources.Remove("AccentFillColorSecondaryBrush");
                resources.Remove("AccentFillColorTertiaryBrush");
            }
            else if (TryNormalizeHex(AppState.Current.AccentHex, out var hex))
            {
                var accent = OverlayWindow.Parse(hex);
                resources["SystemAccentColor"] = accent;
                resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(accent);
                resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(accent);
                resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(accent);
            }
        }
        catch { /* 资源覆盖失败就留系统强调色 */ }
    }

    void ApplySidebar()
    {
        Nav.PaneDisplayMode = AppState.Current.SidebarMode switch
        {
            "left" => NavigationViewPaneDisplayMode.Left,
            "leftcompact" => NavigationViewPaneDisplayMode.LeftCompact,
            _ => NavigationViewPaneDisplayMode.Auto
        };
    }

    void ApplyBackground()
    {
        var s = AppState.Current;
        var path = (s.AppBackground ?? "").Trim();
        // 路径和不透明度都没变就不重新读图：Pull 每次按键都跑，
        // 每回都 new BitmapImage 会一直重新解码。
        if (path == _lastBgPath && Math.Abs(s.AppBgOpacity - _lastBgOpacity) < 0.001) return;
        _lastBgPath = path;
        _lastBgOpacity = s.AppBgOpacity;
        AppBackgroundText.Text = path.Length == 0 ? "没有背景图片。" : "背景图片：" + path;
        if (path.Length == 0 || !File.Exists(path))
        {
            Root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            return;
        }
        try
        {
            Root.Background = new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri(path)),
                Opacity = Math.Clamp(s.AppBgOpacity, 0, 1),
                Stretch = Stretch.UniformToFill
            };
        }
        catch
        {
            Root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            AppBackgroundText.Text = "背景图片打不开：" + path;
        }
    }

    async void PickBackground_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, NativeWin.Hwnd(this));
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        AppState.Current.AppBackground = file.Path;
        AppState.Current.Save();
        ApplyBackground();
    }

    void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        AppState.Current.AppBackground = "";
        AppState.Current.Save();
        ApplyBackground();
    }

    void ApplyVisibility()
    {
        var s = AppState.Current;
        // 先重算全部窗口的扩展样式再决定显隐：ApplyOverlay 末尾的
        // SWP_SHOWWINDOW 会把刚藏起来的窗口重新弹出来，所以 Show/Hide
        // 必须在 ApplyChrome 之后跑，否则悬浮窗永远藏不住。
        _danmaku.ApplyChrome();
        _live.ApplyChrome();
        _lyrics.ApplyChrome();
        _input.ApplyChrome();
        if (s.ShowDanmaku) _danmaku.AppWindow.Show(); else _danmaku.AppWindow.Hide();
        if (s.ShowLive) _live.AppWindow.Show(); else _live.AppWindow.Hide();
        if (s.ShowDesktopLyrics) _lyrics.AppWindow.Show(); else _lyrics.AppWindow.Hide();
        _input.AppWindow.Hide();
    }

    void ApplyTaskbar()
    {
        var hwnd = NativeWin.Hwnd(this);
        var ex = GetEx(hwnd);
        const long tool = 0x00000080;
        const long transparent = 0x20;
        if (AppState.Current.HideFromTaskbar && _tray) ex |= tool;
        else ex &= ~tool;
        // 穿透只属于悬浮窗。任务栏样式重算时也清掉，避免主窗口点不了。
        ex &= ~transparent;
        SetEx(hwnd, ex);
    }

    int _orderTick;
    void KeepOrder()
    {
        // 排序只是每 400ms 的管家活：取句柄失败（窗口还没建好、正在拆）
        // 时跳过这一次，绝不能因为它崩掉整个程序。0.2.0-b1 启动崩溃
        // 的教训：定时器里的未处理异常会直接杀掉应用。
        try { KeepOrderCore(); } catch { /* 跳过这一次，下个 tick 再试 */ }
    }

    void KeepOrderCore()
    {
        var main = NativeWin.Hwnd(this);
        var danmaku = NativeWin.Hwnd(_danmaku);
        var live = NativeWin.Hwnd(_live);
        var lyrics = NativeWin.Hwnd(_lyrics);
        var fg = NativeWin.Foreground();
        // 设置窗口在前台时，先把它放进置顶带，再把悬浮窗插到它下面。
        // 这样悬浮窗仍盖住别的程序，但不会盖住正在点的设置页。
        // 切走之后不再把设置窗口留在 TOPMOST，否则 400ms 内又盖回游戏。
        // 两个置顶开关只管“平时”的状态：主窗口默认不置顶，悬浮窗默认置顶；
        // 正在操作设置窗口的那一刻，主窗口必须临时置顶，否则会被悬浮窗盖住点不到。
        var settingsFocused = AppWindow.IsVisible && (fg == main || fg == _input.Handle);
        var mainTop = AppState.Current.MainTopmost;
        var overlayTop = AppState.Current.OverlayTopmost;
        if (settingsFocused)
        {
            NativeWin.RaiseAbove(main, true);
            if (overlayTop)
            {
                NativeWin.PlaceBelow(danmaku, main);
                NativeWin.PlaceBelow(live, main);
                NativeWin.PlaceBelow(lyrics, main);
            }
            else
            {
                NativeWin.RaiseAbove(danmaku, false);
                NativeWin.RaiseAbove(live, false);
                NativeWin.RaiseAbove(lyrics, false);
            }
        }
        else
        {
            NativeWin.RaiseAbove(main, mainTop);
            NativeWin.RaiseAbove(danmaku, overlayTop);
            NativeWin.RaiseAbove(live, overlayTop);
            NativeWin.RaiseAbove(lyrics, overlayTop);
        }
        if (_input.AppWindow.IsVisible) NativeWin.RaiseAbove(_input.Handle, true);
        _danmaku.Remember();
        _live.Remember();
        _lyrics.Remember();
        if (++_orderTick % 12 == 0) AppState.Current.Save();
    }

    void Sample()
    {
        if (!AppState.Current.AdaptiveText) return;
        // 1.3.1 的 bug：弹幕窗已经隐藏了，采样函数最后的 SW_SHOWNA 还会
        // 把它重新显示出来。先判断可见性，不可见直接跳过。
        if (!_danmaku.AppWindow.IsVisible) return;
        var light = NativeWin.SampleBehindIsLight(NativeWin.Hwnd(_danmaku));
        if (light == null)
        {
            // 采样失败（拿不到屏幕像素）时用 AdaptiveManual 兜底，和 1.2.0
            // adaptive.js 一样：'light' = 背景按亮的算、用深字；'dark' = 反过来。
            // 只有算出来的值真变了才重绘，免得每 2 秒无故重建一次卡片。
            var fallbackDarkText = AppState.Current.AdaptiveManual != "dark";
            if (OverlayWindow.DarkText != fallbackDarkText) { OverlayWindow.DarkText = fallbackDarkText; RefreshCards(); }
            return;
        }
        OverlayWindow.DarkText = light.Value;
        RefreshCards();
    }

    void RefreshCards()
    {
        _danmaku.Render();
        _live.Render();
        MiniPreviewText.Text = AppState.Current.MiniStats
            ? $"点赞 {LiveService.Likes} · 弹幕 {LiveService.Danmaku} · 在线 {LiveService.Online}"
            : "数据栏已隐藏（实时窗设置里可打开）";
        StatusText.Text = AppState.Current.Status;
        AccountText.Text = AppState.Current.Account;
    }


    void ShowPlayer()
    {
        var now = LyricsService.Current;
        PlayerTitle.Text = string.IsNullOrWhiteSpace(now.Title) ? "没有曲目" : now.Title;
        PlayerArtist.Text = string.Join(" · ", new[] { now.Artist, now.Label }.Where(s => !string.IsNullOrWhiteSpace(s)));
        PlayPauseIcon.Glyph = now.Playing ? "\uE769" : "\uE768";
        if (!_updatingSeek && _seekTimer?.IsEnabled != true)
        {
            _updatingSeek = true;
            PlayerSeek.Maximum = now.Duration > 0 ? now.Duration : Math.Max(1, now.Position);
            PlayerSeek.Value = Math.Clamp(now.Position, PlayerSeek.Minimum, PlayerSeek.Maximum);
            _updatingSeek = false;
        }
        PlayerTime.Text = now.Duration > 0 ? $"{FmtTime(now.Position)} / {FmtTime(now.Duration)}" : FmtTime(now.Position);
        var coverKey = now.Title + "\n" + now.Artist + "\n" + (now.Cover?.Length ?? 0);
        if (coverKey != _coverKey)
        {
            _coverKey = coverKey;
            _ = ShowCoverAsync(now.Cover);
        }
        // 全部歌词页开着时跟着进度刷新高亮行（RenderFullLyrics 自己节流，
        // 歌没换、当前行没变时不重建列表，不会把滚动位置弹回顶部）。
        if (PageFullLyrics.Visibility == Visibility.Visible) RenderFullLyrics();
    }

    async Task ShowCoverAsync(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            CoverImage.Source = null;
            CoverFallback.Visibility = Visibility.Visible;
            return;
        }
        try
        {
            var stream = new InMemoryRandomAccessStream();
            var writer = new DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            CoverImage.Source = image;
            CoverFallback.Visibility = Visibility.Collapsed;
        }
        catch
        {
            CoverImage.Source = null;
            CoverFallback.Visibility = Visibility.Visible;
        }
    }

    // 全部歌词页：整页居左列出所有行，当前行高亮；逐字歌词按时间逐字着色。
    // 只在歌换了或当前行变了时重建列表，否则 500ms 的进度 tick 会把
    // 滚动位置弹回顶部。
    void RenderFullLyrics()
    {
        var now = LyricsService.Current;
        FullLyricsSong.Text = string.IsNullOrWhiteSpace(now.Title) ? "" : now.Title + (string.IsNullOrWhiteSpace(now.Artist) ? "" : " — " + now.Artist);
        var key = now.Title + "\n" + now.Artist + "\n" + now.Lines.Count;
        // LineIndexAt 在还没唱到第一行时返回 -1：不要把它钳到 0，
        // 否则节流会认不出“第一行刚开始唱”这次变化，高亮一直不亮。
        var index = now.Lines.Count == 0 ? -1 : LyricsService.LineIndexAt(now.Lines, now.Position);
        if (key == _fullLyricsKey && index == _fullLyricsIndex && FullLyricsList.Children.Count > 0) return;
        _fullLyricsKey = key;
        _fullLyricsIndex = index;
        FullLyricsList.Children.Clear();
        if (now.Lines.Count == 0)
        {
            FullLyricsList.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(now.Current) ? (string.IsNullOrWhiteSpace(now.Label) ? "没有带时间轴的歌词" : now.Label) : now.Current,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            return;
        }
        for (var i = 0; i < now.Lines.Count; i++)
        {
            var line = now.Lines[i];
            var current = i == index;
            FullLyricsList.Children.Add(LyricLineView(line, current, now.Position));
            if (current && !string.IsNullOrWhiteSpace(line.Translation))
                FullLyricsList.Children.Add(new TextBlock { Text = line.Translation, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] });
        }
    }

    // 一行歌词的显示：当前行 16 号半粗体、主色；其余行 14 号、次色。
    // 逐字歌词开着时，当前行按时间逐字着色（唱到的字用强调色）。
    UIElement LyricLineView(LyricLine line, bool current, double position)
    {
        var tertiary = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        if (current && AppState.Current.LyricWordByWord && line.Words.Count > 0)
        {
            var active = LyricsService.WordIndexAt(line, position);
            var accent = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            var rich = new RichTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 16 };
            var para = new Paragraph();
            for (var w = 0; w < line.Words.Count; w++)
                para.Inlines.Add(new Run { Text = line.Words[w].Text, Foreground = w <= active ? accent : tertiary });
            rich.Blocks.Add(para);
            return rich;
        }
        return new TextBlock
        {
            Text = line.Text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = current ? 16 : 14,
            FontWeight = current ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = current ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] : tertiary
        };
    }

    void PlayerCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // 点到播放控制按钮（上一首/播放/下一首）时只走按钮自己的 Click，
        // 别顺带进歌词页；点卡片其他地方才进全部歌词页。
        DependencyObject? el = e.OriginalSource as DependencyObject;
        while (el != null)
        {
            if (el is Button) return;
            el = VisualTreeHelper.GetParent(el);
        }
        ShowPage("fullyrics");
    }

    void FullLyricsBack_Click(object sender, RoutedEventArgs e) => ShowPage("lyrics");

    void PlayerSeek_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSeek || _seekTimer == null) return;
        PlayerTime.Text = LyricsService.Current.Duration > 0
            ? $"{FmtTime(e.NewValue)} / {FmtTime(LyricsService.Current.Duration)}"
            : FmtTime(e.NewValue);
        _seekTimer.Stop();
        _seekTimer.Start();
    }

    static string FmtTime(double seconds)
    {
        var total = Math.Max(0, (int)Math.Round(seconds));
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:D2}:{s:D2}" : $"{m}:{s:D2}";
    }

    void Nav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // 底栏「设置」有时不会改变 SelectedItem，SelectionChanged 就不响。
        // ItemInvoked 只要点到了这项就会来，齿轮和侧栏都走这里。
        if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
            ShowPage(tag);
    }

    void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (!string.IsNullOrEmpty(tag)) ShowPage(tag);
    }

    void ShowPage(string tag)
    {
        PageConn.Visibility = tag == "conn" ? Visibility.Visible : Visibility.Collapsed;
        PageDanmaku.Visibility = tag == "danmaku" ? Visibility.Visible : Visibility.Collapsed;
        PageLive.Visibility = tag == "live" ? Visibility.Visible : Visibility.Collapsed;
        PageLyrics.Visibility = tag == "lyrics" ? Visibility.Visible : Visibility.Collapsed;
        PageFullLyrics.Visibility = tag == "fullyrics" ? Visibility.Visible : Visibility.Collapsed;
        PageSource.Visibility = tag == "source" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "source") { OverlayUrl.Text = ObsServer.OverlayUrl; MiniUrl.Text = ObsServer.MiniUrl; }
        if (tag == "lyrics") ShowPlayer();
        if (tag == "fullyrics") RenderFullLyrics();
    }

    void Connect_Click(object sender, RoutedEventArgs e) { AppState.Current.Room = RoomBox.Text; LiveService.Connect(RoomBox.Text); }
    void Disconnect_Click(object sender, RoutedEventArgs e) => LiveService.Stop();
    async void OwnRoom_Click(object sender, RoutedEventArgs e)
    {
        var (ok, message) = await LiveService.ReadOwnRoom();
        StatusText.Text = ok ? "已读到房间 " + message : message;
        if (ok) { RoomBox.Text = message; AppState.Current.Room = message; AppState.Current.Save(); }
    }
    async void SaveCookie_Click(object sender, RoutedEventArgs e)
    {
        // #36：手动粘贴 Cookie，和扫码登录走同一条保存路径
        //（写 AppState.Cookie → 读账号昵称 → 落盘）。
        AppState.Current.Cookie = CookieBox.Text.Trim();
        AppState.Current.Save();
        await LiveService.RefreshAccount();
        AccountText.Text = AppState.Current.Account;
        StatusText.Text = "已保存 Cookie";
    }

    void Logout_Click(object sender, RoutedEventArgs e)
    {
        // #36 退出登录：清 Cookie + 账号，断开连接。房间号和其它设置不动。
        LiveService.Stop();
        AppState.Current.Cookie = "";
        AppState.Current.Account = "未登录";
        AppState.Current.Save();
        CookieBox.Text = "";
        AccountText.Text = "未登录";
        StatusText.Text = "已退出登录";
    }

    async void Qr_Click(object sender, RoutedEventArgs e)
    {
        var (url, message) = await LiveService.StartQr(cookie => App.UI(() =>
        {
            AppState.Current.Cookie = cookie;
            _ = LiveService.RefreshAccount().ContinueWith(_ => App.UI(() => AccountText.Text = AppState.Current.Account));
            AppState.Current.Save();
            AccountText.Text = "已登录";
            QrImage.Visibility = Visibility.Collapsed;
        }));
        StatusText.Text = message;
        if (url == null) return;
        var png = new PngByteQRCode(new QRCodeGenerator().CreateQrCode(url, QRCodeGenerator.ECCLevel.Q)).GetGraphic(8);
        var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(png);
        await writer.StoreAsync();
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        QrImage.Source = image;
        QrImage.Visibility = Visibility.Visible;
    }
    void Test_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((Button)sender).Tag;
        // 测试消息只发给悬浮窗：目标窗藏着时，点了就像没反应。
        // 先按这条消息实际会去的窗口把它打开（开关跟着变并保存），再发消息。
        // 进场/关注/点赞/分享按各自的“互动去向”走：mini 去实时窗，main 去弹幕窗；
        // 去向是“关闭”时消息会被直接丢掉，这是设置本身的意思，不是测试坏了。
        var s = AppState.Current;
        var toLive = tag switch
        {
            "enter" => s.RouteEnter == "mini",
            "follow" => s.RouteFollow == "mini",
            "like" => s.RouteLike == "mini",
            "share" => s.RouteShare == "mini",
            _ => false,
        };
        if (toLive) { if (!s.ShowLive) LiveSwitch.IsOn = true; }
        else if (!s.ShowDanmaku) DanmakuWindowSwitch.IsOn = true;
        ApplyVisibility();
        if (tag == "emote")
        {
            // 1.2.0 演示里的 [dog] 表情，不用进直播间就能看到行内表情的效果。
            LiveService.PushLocal("danmaku", "预览", "[dog]", force: true, emote: "https://i0.hdslb.com/bfs/live/4428c84e694fbf4e0ef6c06e958d9352c3582740.png");
            return;
        }
        // #38：测试消息要完整——礼物带价格（金瓜子）、醒目留言带价格 +
        // B 站底色、上舰带 GuardLevel（徽章才显示）。进场/关注/点赞/分享
        // 和以前一样走 PushLocal；数值照 1.2.0 samples.js 的 sample()。
        switch (tag)
        {
            case "gift":
                LiveService.PushLine(new ChatLine { Type = "gift", Name = "预览", Text = "小花花 x10（1000金瓜子）", Price = 1000, MedalName = "粉丝团", MedalLevel = 12, MedalColor = "#2D0855" }, force: true);
                break;
            case "superchat":
                LiveService.PushLine(new ChatLine { Type = "superchat", Name = "预览", Text = "醒目留言", Price = 30, Color = "#2A60B2", MedalName = "粉丝团", MedalLevel = 24, MedalColor = "#2D0855" }, force: true);
                break;
            case "guard":
                LiveService.PushLine(new ChatLine { Type = "guard", Name = "预览", Text = "舰长 x1", Price = 198, GuardLevel = 3, MedalName = "粉丝团", MedalLevel = 22, MedalColor = "#2D0855" }, force: true);
                break;
            default:
                LiveService.PushLocal(tag, "预览", tag switch { "enter" => "进入直播间", "follow" => "关注了", "like" => "点赞了", "share" => "分享了", _ => "这是一条测试弹幕" }, force: true);
                break;
        }
    }
    void PreviewSpeech_Click(object sender, RoutedEventArgs e)
    {
        SpeechService.Speak("这是 TokiLive 的试听", force: true);
    }
    async void LyricControl_Click(object sender, RoutedEventArgs e) => await LyricsService.Control((string)((Button)sender).Tag);
    async void ImportLyrics_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, NativeWin.Hwnd(this));
        picker.FileTypeFilter.Add(".lrc");
        picker.FileTypeFilter.Add(".ttml");
        picker.FileTypeFilter.Add(".xml");
        picker.FileTypeFilter.Add(".txt");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        var text = await Windows.Storage.FileIO.ReadTextAsync(file);
        // #45：先校验再存——0 行带时间戳的文件直接弹框拒绝，不覆盖已有的
        // LyricLocal；本地歌词最多存 200000 字，超了截断。
        if (!LyricsService.TryParseLyrics(text, out var lineCount))
        {
            var dialog = new ContentDialog
            {
                Title = "TokiLive",
                Content = $"这个文件里没有带时间的歌词行（共 {lineCount} 行），没有导入。",
                CloseButtonText = "知道了",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
            return;
        }
        if (text.Length > 200000) text = text[..200000];
        AppState.Current.LyricLocal = text;
        AppState.Current.Save();
        await LyricsService.Refresh();
        ShowPlayer();
    }
    async void ClearLyrics_Click(object sender, RoutedEventArgs e)
    {
        // 本地歌词一旦导入就永久优先于在线歌词：没有清除入口的话，
        // 换歌也一直显示旧的那份。这里清掉，回到在线歌词。
        AppState.Current.LyricLocal = "";
        AppState.Current.Save();
        await LyricsService.Refresh();
        ShowPlayer();
    }
    async void Update_Click(object sender, RoutedEventArgs e)
    {
        await UpdateService.Check(_updateArmed);
        _updateArmed = AppState.Current.UpdateMessage.StartsWith("可以更新", StringComparison.Ordinal);
        UpdateText.Text = AppState.Current.UpdateMessage;
        // #35：下载结束了（成败都算），进度条收起来；有新版说明就展开显示。
        UpdateProgress.Visibility = Visibility.Collapsed;
        UpdateProgressText.Visibility = Visibility.Collapsed;
        var notes = UpdateService.LatestNotes;
        UpdateNotesExpander.Visibility = string.IsNullOrWhiteSpace(notes) ? Visibility.Collapsed : Visibility.Visible;
        UpdateNotesText.Text = notes ?? "";
    }
    void TestBar_Wheel(object sender, PointerRoutedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        if (sv.ScrollableWidth <= 0) return;
        sv.ChangeView(sv.HorizontalOffset - e.GetCurrentPoint(sv).Properties.MouseWheelDelta, null, null, true);
        e.Handled = true;
    }

    // ComboBoxItem 的值优先读 Tag（细/常规/粗/特粗这种中文显示配英文值），
    // 没有 Tag 就读 Content（对齐那组就是直接拿 Content 当值）。
    static string? ComboValue(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string ?? (box.SelectedItem as ComboBoxItem)?.Content as string;

    static void SelectCombo(ComboBox box, string value)
    {
        foreach (var item in box.Items)
            if (item is ComboBoxItem ci && ((ci.Tag as string) ?? ci.Content as string) == value)
            {
                box.SelectedItem = item;
                return;
            }
    }

    // 字体只选不输：四个字体下拉框都用这组方法。FillFontCombo 只在加载时
    // 调一次（把 KnownFonts 填进去）；SelectFont 选值，保存的值不在列表里
    //（以前手输的）就先加进去再选，不丢用户已有的字体。
    static void FillFontCombo(ComboBox box)
    {
        box.Items.Clear();
        foreach (var font in FontHelper.KnownFonts) box.Items.Add(font);
    }

    static void SelectFont(ComboBox box, string? font)
    {
        if (string.IsNullOrWhiteSpace(font)) { box.SelectedItem = null; return; }
        if (!box.Items.Contains(font)) box.Items.Add(font);
        box.SelectedItem = font;
    }

    // 选设计：把设计表里的值一次写进表单控件，再走 Pull() 保存并
    // 刷新歌词窗。_loading 期间（LoadForm）直接返回，不盖掉已保存的单项。
    // 选完之后再改单项设置，设计名还留在那，不会把改动弹回去。
    void LyricDesignBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LyricDesignBox.SelectedItem == null) return;
        var design = LyricsDesigns.ById(ComboValue(LyricDesignBox));
        _loading = true;
        SelectFont(LyricFontBox, design.Font);
        LyricSizeSlider.Value = design.Size;
        LyricColorBox.Text = design.Color;
        LyricHiBox.Text = design.Highlight;
        SelectCombo(LyricWeightBox, design.Weight);
        LyricShadowSwitch.IsOn = design.Shadow;
        LyricOutlineSwitch.IsOn = design.Outline;
        LyricBgSlider.Value = design.Bg;
        LyricAlignBox.SelectedIndex = design.Align == "left" ? 0 : design.Align == "right" ? 2 : 1;
        _loading = false;
        Pull();
        _lyrics.Render();
    }

    // 1.2.0 settings.html:300-307 的 7 个预设。WinUI 没有 1.2.0 的
    // design（边距/间距/元素显隐）和 customCss，也没有 blur 这一项，
    // 所以只按下表里 WinUI 能表达的部分映射，数值有来源的直接用来源值：
    // 默认：pad/gap/spacing 都是 1；紧凑：pad .72、gap .55、spacing .8（更小）；
    // 极简：hide medal+badge、无描边；霓虹：名字用主题色 --md-primary 发光描边；
    // 纯文字：hide medal+badge、背景全无、白字；大字：fontSize 22；
    // 安静：opacity 0.72、blur 0、hide medal+badge。
    // “藏粉丝牌/徽章”在 WinUI 里没有对应开关，就用头像开关（关掉头像圆）
    // 和边框开关来近似；每个预设都把字号、圆角、不透明度、头像、
    // 自适应、边框和 8 个消息开关一次设全，免得切了预设还留着上一个的状态。
    void Preset_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((FrameworkElement)sender).Tag;
        _loading = true;
        void AllMessages(bool danmaku = true, bool gift = true, bool sc = true, bool guard = true,
            bool enter = false, bool follow = true, bool like = true, bool share = true)
        {
            MsgDanmaku.IsOn = danmaku; MsgGift.IsOn = gift; MsgSc.IsOn = sc; MsgGuard.IsOn = guard;
            MsgEnter.IsOn = enter; MsgFollow.IsOn = follow; MsgLike.IsOn = like; MsgShare.IsOn = share;
        }
        switch (tag)
        {
            case "极简": // hide medal+badge、无描边：关头像和边框，圆角收小
                FontSlider.Value = 16; CornerSlider.Value = 8; OpacitySlider.Value = 0.86;
                AvatarSwitch.IsOn = false; AdaptiveSwitch.IsOn = false; BorderSwitch.IsOn = false;
                AllMessages(); break;
            case "霓虹": // 名字用主题色、发光描边：开强调色边框，名字改成种子色
                FontSlider.Value = 18; CornerSlider.Value = 16; OpacitySlider.Value = 0.92;
                NameColorBox.Text = "#6750A4";
                AvatarSwitch.IsOn = true; AdaptiveSwitch.IsOn = false; BorderSwitch.IsOn = true;
                AllMessages(); break;
            default: // 默认：字号 18（1.2.0 默认）、圆角 16、不透明度 0.86，颜色和开关全重置
                FontSlider.Value = 18; CornerSlider.Value = 16; OpacitySlider.Value = 0.86;
                NameColorBox.Text = "#7C4DFF"; TextColorBox.Text = "#1A1A1A"; CardColorBox.Text = "#F5F5F5"; SelectFont(CardFontBox, "Inter");
                AvatarSwitch.IsOn = true; AdaptiveSwitch.IsOn = false; BorderSwitch.IsOn = false;
                AllMessages(); break;
        }
        _loading = false;
        Pull();
    }

    async void ExportTheme_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, NativeWin.Hwnd(this));
        picker.SuggestedFileName = "TokiLive-theme";
        picker.FileTypeChoices.Add("JSON", new System.Collections.Generic.List<string> { ".json" });
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        var s = AppState.Current;
        var json = System.Text.Json.JsonSerializer.Serialize(new CardTheme(s.NameColor, s.TextColor, s.CardColor, s.CardFont, s.FontSize, s.Corner, s.CardOpacity, s.ShowAvatar));
        await Windows.Storage.FileIO.WriteTextAsync(file, json);
        StatusText.Text = "已导出主题";
    }

    async void ImportTheme_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, NativeWin.Hwnd(this));
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        CardTheme? theme;
        try { theme = System.Text.Json.JsonSerializer.Deserialize<CardTheme>(await Windows.Storage.FileIO.ReadTextAsync(file)); }
        catch { StatusText.Text = "主题文件读不出来"; return; }
        if (theme == null) { StatusText.Text = "主题文件读不出来"; return; }
        _loading = true;
        NameColorBox.Text = theme.NameColor; TextColorBox.Text = theme.TextColor; CardColorBox.Text = theme.CardColor;
        SelectFont(CardFontBox, theme.CardFont); FontSlider.Value = theme.FontSize; CornerSlider.Value = theme.Corner;
        OpacitySlider.Value = theme.CardOpacity; AvatarSwitch.IsOn = theme.ShowAvatar;
        _loading = false;
        Pull();
        StatusText.Text = "已导入主题";
    }

    void ResetBounds_Click(object sender, RoutedEventArgs e)
    {
        var s = AppState.Current;
        s.DanmakuW = s.DanmakuH = s.LiveW = s.LiveH = s.LyricW = s.LyricH = 0;
        _danmaku.RestoreBounds();
        _live.RestoreBounds();
        _lyrics.RestoreBounds();
        s.Save();
    }

    const int GWL_EXSTYLE = -20;
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint h, int i);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint h, int i, nint v);
    static long GetEx(nint hwnd) => GetWindowLongPtr(hwnd, GWL_EXSTYLE);
    static void SetEx(nint hwnd, long value) => SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)value);
}
