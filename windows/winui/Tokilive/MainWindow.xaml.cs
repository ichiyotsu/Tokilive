using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
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
    DispatcherTimer? _z;
    DispatcherTimer? _sample;
    DispatcherTimer? _lyricTimer;
    bool _updateArmed;

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
        Closed += (_, _) =>
        {
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
        LiveService.Lines.CollectionChanged += (_, _) => App.UI(RefreshCards);
        LiveService.Pills.CollectionChanged += (_, _) => App.UI(RefreshCards);
        _ = LiveService.RefreshAccount().ContinueWith(_ => App.UI(() => { if (AccountText != null) AccountText.Text = AppState.Current.Account; }));
        var hwnd = NativeWin.Hwnd(this);
        _tray = NativeWin.TryTray(hwnd, () => { Activate(); AppWindow.Show(); });
        if (!_tray) AppState.Current.HideFromTaskbar = false;
        ApplyTaskbar();
        Hotkey.Bind(hwnd, () => _input.Toggle());
        _z = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _z.Tick += (_, _) => KeepOrder();
        _z.Start();
        _sample = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sample.Tick += (_, _) => Sample();
        _sample.Start();
        _lyricTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lyricTimer.Tick += async (_, _) => { try { await LyricsService.Refresh(); ShowPlayer(); _lyrics.Render(); } catch { } };
        _lyricTimer.Start();
        ApplyVisibility();
        RefreshCards();
        ObsServer.Apply();
    }

    void TryBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
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
        AvatarSwitch.IsOn = s.ShowAvatar;
        NameColorBox.Text = s.NameColor;
        CardColorBox.Text = s.CardColor;
        TextColorBox.Text = s.TextColor;
        CardFontBox.Text = s.CardFont;
        FontSlider.Value = s.FontSize;
        AdaptiveSwitch.IsOn = s.AdaptiveText;
        CornerSlider.Value = s.Corner;
        OpacitySlider.Value = s.CardOpacity;
        BorderSwitch.IsOn = s.ShowWindowBorder;
        ClickSwitch.IsOn = s.ClickThrough;
        GameTopSwitch.IsOn = s.ExperimentalTopmost;
        MsgDanmaku.IsOn = s.ShowDanmakuMsg;
        MsgGift.IsOn = s.ShowGift;
        MsgSc.IsOn = s.ShowSuperchat;
        MsgGuard.IsOn = s.ShowGuard;
        MsgEnter.IsOn = s.ShowEnter;
        MsgFollow.IsOn = s.ShowFollow;
        MsgLike.IsOn = s.ShowLike;
        MsgShare.IsOn = s.ShowShare;
        HotkeyBox.Text = s.Hotkey;
        TtsSwitch.IsOn = s.TtsEnabled;
        RateSlider.Value = s.TtsRate;
        VolumeSlider.Value = s.TtsVolume;
        foreach (var voice in SpeechService.Voices()) VoiceBox.Items.Add(voice);
        if (VoiceBox.Items.Count == 0) VoiceBox.Items.Add("系统默认");
        VoiceBox.SelectedIndex = 0;
        LiveSwitch.IsOn = s.ShowLive;
        LiveBorderSwitch.IsOn = s.ShowLiveBorder;
        DesktopLyricsSwitch.IsOn = s.ShowDesktopLyrics;
        LyricDemoSwitch.IsOn = s.LyricDemo;
        LyricFontBox.Text = s.LyricFont;
        LyricSizeSlider.Value = s.LyricSize;
        LyricColorBox.Text = s.LyricColor;
        LyricHiBox.Text = s.LyricHighlight;
        LyricAlignBox.SelectedIndex = s.LyricAlign == "left" ? 0 : s.LyricAlign == "right" ? 2 : 1;
        LyricShadowSwitch.IsOn = s.LyricShadow;
        LyricOutlineSwitch.IsOn = s.LyricOutline;
        LyricBgSlider.Value = s.LyricBg;
        LyricLinesSlider.Value = s.LyricLines;
        LyricClickSwitch.IsOn = s.LyricClickThrough;
        LyricLockSwitch.IsOn = s.LyricLocked;
        LyricOffsetSlider.Value = s.LyricOffset;
        DanmakuWindowSwitch.IsOn = s.ShowDanmaku;
        TaskbarSwitch.IsOn = s.HideFromTaskbar;
        ObsSwitch.IsOn = s.ObsEnabled;
        PortBox.Value = s.ObsPort;
        ThemeBox.SelectedIndex = s.ThemeMode;
        AccentSwitch.IsOn = s.UseSystemAccent;
        AccentBox.Text = s.AccentHex;
        UpdateText.Text = s.UpdateMessage;
        Wire();
        ApplyTheme();
    }

    void Wire()
    {
        void Save(object? _, object? __) { if (!_loading) Pull(); }
        foreach (var box in new TextBox[] { RoomBox, NameColorBox, CardColorBox, TextColorBox, CardFontBox, HotkeyBox, LyricFontBox, LyricColorBox, LyricHiBox, AccentBox })
            box.TextChanged += (_, _) => Save(null, null);
        foreach (var sw in new ToggleSwitch[] { AvatarSwitch, AdaptiveSwitch, BorderSwitch, ClickSwitch, GameTopSwitch, MsgDanmaku, MsgGift, MsgSc, MsgGuard, MsgEnter, MsgFollow, MsgLike, MsgShare, TtsSwitch, LiveSwitch, LiveBorderSwitch, DesktopLyricsSwitch, LyricDemoSwitch, LyricShadowSwitch, LyricOutlineSwitch, LyricClickSwitch, LyricLockSwitch, DanmakuWindowSwitch, TaskbarSwitch, ObsSwitch, AccentSwitch })
            sw.Toggled += (_, _) => Save(null, null);
        foreach (var slider in new Slider[] { FontSlider, CornerSlider, OpacitySlider, RateSlider, VolumeSlider, LyricSizeSlider, LyricBgSlider, LyricLinesSlider, LyricOffsetSlider })
            slider.ValueChanged += (_, _) => Save(null, null);
        ThemeBox.SelectionChanged += (_, _) => Save(null, null);
        LyricAlignBox.SelectionChanged += (_, _) => Save(null, null);
        VoiceBox.SelectionChanged += (_, _) => Save(null, null);
        PortBox.ValueChanged += (_, _) => Save(null, null);
        TtsSwitch.Toggled += (_, _) => { if (!TtsSwitch.IsOn) SpeechService.Stop(); };
    }

    void Pull()
    {
        var s = AppState.Current;
        var wasTts = s.TtsEnabled;
        s.Room = RoomBox.Text;
        s.ShowAvatar = AvatarSwitch.IsOn;
        s.NameColor = NameColorBox.Text;
        s.CardColor = CardColorBox.Text;
        s.TextColor = TextColorBox.Text;
        s.CardFont = CardFontBox.Text;
        s.FontSize = FontSlider.Value;
        s.AdaptiveText = AdaptiveSwitch.IsOn;
        s.Corner = CornerSlider.Value;
        s.CardOpacity = OpacitySlider.Value;
        s.ShowWindowBorder = BorderSwitch.IsOn;
        s.ClickThrough = ClickSwitch.IsOn;
        s.ExperimentalTopmost = GameTopSwitch.IsOn;
        s.ShowDanmakuMsg = MsgDanmaku.IsOn;
        s.ShowGift = MsgGift.IsOn;
        s.ShowSuperchat = MsgSc.IsOn;
        s.ShowGuard = MsgGuard.IsOn;
        s.ShowEnter = MsgEnter.IsOn;
        s.ShowFollow = MsgFollow.IsOn;
        s.ShowLike = MsgLike.IsOn;
        s.ShowShare = MsgShare.IsOn;
        s.Hotkey = HotkeyBox.Text;
        s.TtsEnabled = TtsSwitch.IsOn;
        s.TtsRate = RateSlider.Value;
        s.TtsVolume = VolumeSlider.Value;
        s.TtsVoice = VoiceBox.SelectedItem as string ?? "";
        s.ShowLive = LiveSwitch.IsOn;
        s.ShowLiveBorder = LiveBorderSwitch.IsOn;
        s.ShowDesktopLyrics = DesktopLyricsSwitch.IsOn;
        s.LyricDemo = LyricDemoSwitch.IsOn;
        s.LyricFont = LyricFontBox.Text;
        s.LyricSize = LyricSizeSlider.Value;
        s.LyricColor = LyricColorBox.Text;
        s.LyricHighlight = LyricHiBox.Text;
        s.LyricAlign = (LyricAlignBox.SelectedItem as ComboBoxItem)?.Content as string ?? "center";
        s.LyricShadow = LyricShadowSwitch.IsOn;
        s.LyricOutline = LyricOutlineSwitch.IsOn;
        s.LyricBg = LyricBgSlider.Value;
        s.LyricLines = (int)LyricLinesSlider.Value;
        s.LyricClickThrough = LyricClickSwitch.IsOn;
        s.LyricLocked = LyricLockSwitch.IsOn;
        s.LyricOffset = LyricOffsetSlider.Value;
        s.ShowDanmaku = DanmakuWindowSwitch.IsOn;
        s.HideFromTaskbar = TaskbarSwitch.IsOn && _tray;
        s.ObsEnabled = ObsSwitch.IsOn;
        s.ObsPort = (int)PortBox.Value;
        s.ThemeMode = ThemeBox.SelectedIndex < 0 ? 0 : ThemeBox.SelectedIndex;
        s.UseSystemAccent = AccentSwitch.IsOn;
        s.AccentHex = AccentBox.Text;
        if (wasTts && !s.TtsEnabled) SpeechService.Stop();
        s.Save();
        ApplyTheme();
        ApplyVisibility();
        ApplyTaskbar();
        Hotkey.Bind(NativeWin.Hwnd(this), () => _input.Toggle());
        ObsServer.Apply();
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
    }

    void ApplyVisibility()
    {
        var s = AppState.Current;
        if (s.ShowDanmaku) _danmaku.AppWindow.Show(); else _danmaku.AppWindow.Hide();
        if (s.ShowLive) _live.AppWindow.Show(); else _live.AppWindow.Hide();
        if (s.ShowDesktopLyrics) _lyrics.AppWindow.Show(); else _lyrics.AppWindow.Hide();
        _input.AppWindow.Hide();
        _danmaku.ApplyChrome();
        _live.ApplyChrome();
        _lyrics.ApplyChrome();
    }

    void ApplyTaskbar()
    {
        var hwnd = NativeWin.Hwnd(this);
        var ex = GetEx(hwnd);
        const long tool = 0x00000080;
        if (AppState.Current.HideFromTaskbar && _tray) ex |= tool;
        else ex &= ~tool;
        SetEx(hwnd, ex);
    }

    int _orderTick;
    void KeepOrder()
    {
        var main = NativeWin.Hwnd(this);
        if (AppState.Current.ExperimentalTopmost)
        {
            NativeWin.RaiseAbove(NativeWin.Hwnd(_danmaku), true);
            NativeWin.RaiseAbove(NativeWin.Hwnd(_live), true);
        }
        NativeWin.PlaceBelow(NativeWin.Hwnd(_danmaku), main);
        NativeWin.PlaceBelow(NativeWin.Hwnd(_live), main);
        NativeWin.PlaceBelow(NativeWin.Hwnd(_lyrics), main);
        if (_input.AppWindow.IsVisible) NativeWin.RaiseAbove(_input.Handle, true);
        _danmaku.Remember();
        _live.Remember();
        _lyrics.Remember();
        if (++_orderTick % 12 == 0) AppState.Current.Save();
    }

    void Sample()
    {
        if (!AppState.Current.AdaptiveText) return;
        var light = NativeWin.SampleBehindIsLight(NativeWin.Hwnd(_danmaku));
        if (light == null) return;
        OverlayWindow.DarkText = light.Value;
        RefreshCards();
    }

    void RefreshCards()
    {
        _danmaku.Render();
        _live.Render();
        PreviewList.Children.Clear();
        var lines = LiveService.Lines.TakeLast(4).ToList();
        if (lines.Count == 0) PreviewList.Children.Add(OverlayWindow.Card("预览", "头像、文字和圆角都作用在这一块", AppState.Current));
        foreach (var line in lines) PreviewList.Children.Add(OverlayWindow.Card(line.Name, line.Text, AppState.Current));
        LivePreview.Text = $"点赞 {LiveService.Likes}    弹幕 {LiveService.Danmaku}    在线 {LiveService.Online}\n{LiveService.Watched}";
        StatusText.Text = AppState.Current.Status;
        AccountText.Text = AppState.Current.Account;
    }

    void ShowPlayer()
    {
        var now = LyricsService.Current;
        PlayerTitle.Text = now.Title;
        PlayerArtist.Text = string.Join(" · ", new[] { now.Artist, now.Label, now.Source }.Where(s => !string.IsNullOrWhiteSpace(s)));
        PlayerLine.Text = string.IsNullOrWhiteSpace(now.Current) ? "没有带时间轴的歌词" : now.Current;
    }

    void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "conn";
        PageConn.Visibility = tag == "conn" ? Visibility.Visible : Visibility.Collapsed;
        PageDanmaku.Visibility = tag == "danmaku" ? Visibility.Visible : Visibility.Collapsed;
        PageLive.Visibility = tag == "live" ? Visibility.Visible : Visibility.Collapsed;
        PageLyrics.Visibility = tag == "lyrics" ? Visibility.Visible : Visibility.Collapsed;
        PageSource.Visibility = tag == "source" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "source") { OverlayUrl.Text = ObsServer.OverlayUrl; MiniUrl.Text = ObsServer.MiniUrl; }
        if (tag == "lyrics") ShowPlayer();
    }

    void Connect_Click(object sender, RoutedEventArgs e) { AppState.Current.Room = RoomBox.Text; LiveService.Connect(RoomBox.Text); }
    void Disconnect_Click(object sender, RoutedEventArgs e) => LiveService.Stop();
    async void OwnRoom_Click(object sender, RoutedEventArgs e)
    {
        var (ok, message) = await LiveService.ReadOwnRoom();
        StatusText.Text = ok ? "已读到房间 " + message : message;
        if (ok) { RoomBox.Text = message; AppState.Current.Room = message; AppState.Current.Save(); }
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
        LiveService.PushLocal(tag, "预览", tag switch { "gift" => "小礼物 x1", "superchat" => "醒目留言", "guard" => "舰长", "enter" => "进入直播间", "follow" => "关注了", "like" => "点赞了", "share" => "分享了", _ => "这是一条测试弹幕" }, force: true);
    }
    void PreviewSpeech_Click(object sender, RoutedEventArgs e)
    {
        SpeechService.Speak("这是 tokilive 的试听", force: true);
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
        AppState.Current.LyricLocal = await Windows.Storage.FileIO.ReadTextAsync(file);
        AppState.Current.Save();
        await LyricsService.Refresh();
        ShowPlayer();
    }
    async void Update_Click(object sender, RoutedEventArgs e)
    {
        await UpdateService.Check(_updateArmed);
        _updateArmed = AppState.Current.UpdateMessage.StartsWith("可以更新", StringComparison.Ordinal);
        UpdateText.Text = AppState.Current.UpdateMessage;
    }
    void TestBar_Wheel(object sender, PointerRoutedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        if (sv.ScrollableWidth <= 0) return;
        sv.ChangeView(sv.HorizontalOffset - e.GetCurrentPoint(sv).Properties.MouseWheelDelta, null, null, true);
        e.Handled = true;
    }

    void Preset_Click(object sender, RoutedEventArgs e)
    {
        var tag = (string)((FrameworkElement)sender).Tag;
        _loading = true;
        if (tag == "大字") { FontSlider.Value = 28; CornerSlider.Value = 20; OpacitySlider.Value = 0.92; }
        else if (tag == "安静")
        {
            FontSlider.Value = 14; OpacitySlider.Value = 0.55;
            MsgEnter.IsOn = MsgFollow.IsOn = MsgLike.IsOn = MsgShare.IsOn = false;
        }
        else
        {
            FontSlider.Value = 16; CornerSlider.Value = 16; OpacitySlider.Value = 0.86;
            NameColorBox.Text = "#7C4DFF"; TextColorBox.Text = "#1A1A1A"; CardColorBox.Text = "#F5F5F5"; CardFontBox.Text = "Segoe UI Variable";
            MsgDanmaku.IsOn = MsgGift.IsOn = MsgSc.IsOn = MsgGuard.IsOn = MsgFollow.IsOn = MsgLike.IsOn = MsgShare.IsOn = true;
            MsgEnter.IsOn = false;
        }
        _loading = false;
        Pull();
    }

    async void ExportTheme_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, NativeWin.Hwnd(this));
        picker.SuggestedFileName = "tokilive-theme";
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
        CardFontBox.Text = theme.CardFont; FontSlider.Value = theme.FontSize; CornerSlider.Value = theme.Corner;
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
