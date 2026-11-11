using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Tokilive;

public sealed partial class OverlayWindow : Window
{
    public enum Kind { Danmaku, Live, Lyrics }
    readonly Kind _kind;
    public static bool DarkText = true;

    public OverlayWindow(Kind kind)
    {
        _kind = kind;
        InitializeComponent();
        var title = kind switch { Kind.Live => "tokilive · 实时窗", Kind.Lyrics => "tokilive · 歌词", _ => "tokilive · 弹幕" };
        NativeWin.ApplyOverlay(this, title, ClickThrough, resizable: kind != Kind.Lyrics, noActivate: true);
        RestoreBounds();
        Closed += (_, _) => { };
        // 1.3.1 的悬浮窗没有标题栏、没有任何拖动处理，穿透一关也挪不动位置。
        // 1.2.0 解锁时整个窗口都是拖拽区。这里在不穿透时点住窗口就拖动。
        Root.PointerPressed += (_, _) =>
        {
            if (!ClickThrough) NativeWin.DragWindow(NativeWin.Hwnd(this));
        };
        Render();
    }

    public void RestoreBounds()
    {
        var state = AppState.Current;
        var (x, y, w, h) = _kind switch
        {
            Kind.Live => (state.LiveX, state.LiveY, state.LiveW, state.LiveH),
            Kind.Lyrics => (state.LyricX, state.LyricY, state.LyricW, state.LyricH),
            _ => (state.DanmakuX, state.DanmakuY, state.DanmakuW, state.DanmakuH)
        };
        if (w < 200 || h < 80)
        {
            x = 80 + (int)_kind * 40;
            y = 80;
            // 1.2.0 的实时窗默认是 460x60 的横条（miniLayout 'h'），
            // 1.3.1 默认给了 420x160 的竖卡片，数字只占一行、下面全是空的。
            // 弹幕窗默认 420x640（1.2.0），1.3.1 的 420x280 装不下 8 条卡片。
            w = _kind == Kind.Lyrics ? 760 : _kind == Kind.Live ? 460 : 420;
            h = _kind == Kind.Live ? 64 : _kind == Kind.Lyrics ? 120 : 640;
        }
        // 1.2.0 恢复窗口位置时会钳制到当前显示器工作区；1.3.1 不做，
        // 换显示器/改分辨率后窗口可能落在屏外找不回来。
        try
        {
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            w = Math.Min(w, area.Width);
            h = Math.Min(h, area.Height);
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - 100));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - 100));
        }
        catch { /* 拿不到显示器信息就按原坐标放 */ }
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(x, y, w, h));
    }

    public void Remember()
    {
        if (!AppWindow.IsVisible) return;
        var p = AppWindow.Position;
        var s = AppWindow.Size;
        var state = AppState.Current;
        if (_kind == Kind.Live) { state.LiveX = p.X; state.LiveY = p.Y; state.LiveW = s.Width; state.LiveH = s.Height; }
        else if (_kind == Kind.Lyrics) { state.LyricX = p.X; state.LyricY = p.Y; state.LyricW = s.Width; state.LyricH = s.Height; }
        else { state.DanmakuX = p.X; state.DanmakuY = p.Y; state.DanmakuW = s.Width; state.DanmakuH = s.Height; }
    }

    bool ClickThrough => _kind switch
    {
        Kind.Lyrics => AppState.Current.LyricClickThrough,
        _ => AppState.Current.ClickThrough
    };

    public void ApplyChrome()
    {
        var locked = _kind == Kind.Lyrics && AppState.Current.LyricLocked;
        NativeWin.ApplyOverlay(this, AppWindow.Title, ClickThrough || locked, resizable: !locked && _kind != Kind.Lyrics, noActivate: true);
        var border = _kind == Kind.Live ? AppState.Current.ShowLiveBorder : _kind == Kind.Danmaku && AppState.Current.ShowWindowBorder;
        Root.BorderBrush = border ? new SolidColorBrush(Parse(AppState.Current.UseSystemAccent ? NativeWin.SystemAccent() ?? AppState.Current.AccentHex : AppState.Current.AccentHex)) : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        Root.BorderThickness = border ? new Thickness(2) : new Thickness(0);
    }

    public void Render()
    {
        Host.Children.Clear();
        var state = AppState.Current;
        var now = DateTimeOffset.UtcNow;
        if (_kind == Kind.Danmaku)
        {
            // 1.2.0：最多 30 条、30 秒后淡出。1.3.1 只取最后 8 条、永不过期，
            // 旧消息会一直占着位置。按时间过滤，过期由 MainWindow 每秒的
            // 定时器触发重绘（见 HasExpired）。
            var lines = LiveService.Lines.Where(l => now - l.At <= LiveService.DanmakuLifetime).TakeLast(8).ToList();
            if (lines.Count == 0) Host.Children.Add(Card("等待弹幕", "连接后会显示在这里", state));
            foreach (var line in lines) Host.Children.Add(Card(line, state));
        }
        else if (_kind == Kind.Live)
        {
            Host.Children.Add(Card("实时", $"点赞 {FormatCount(LiveService.Likes)}   弹幕 {FormatCount(LiveService.Danmaku)}   在线 {FormatCount(LiveService.Online)}\n{LiveService.Watched}", state));
            // 1.2.0 的实时窗互动（进场/关注/点赞/分享）6 秒后淡出、最多 5 条。
            foreach (var line in LiveService.Pills.Where(l => now - l.At <= LiveService.PillLifetime).TakeLast(4)) Host.Children.Add(Card(line, state));
            if (LiveService.Pills.Count == 0) Host.Children.Add(Card("互动", "进场、关注、点赞和分享出现在这里", state));
        }
        else
        {
            var now = LyricsService.Current;
            // 只取一次：以前这里 TakeLast 写了两遍。当前行按时间下标定——
            // 取 shown 里 Time <= 当前播放位置的最后一行，不再按文本相等比，
            // 歌词重复出现时前面那行不会被误标成当前行。
            // （和 LyricsService.LineAt 同一个规则：Time <= 位置的最后一行。）
            var shown = now.Lines.TakeLast(Math.Max(1, state.LyricLines)).ToList();
            if (shown.Count == 0) shown.Add(new LyricLine { Text = string.IsNullOrEmpty(now.Current) ? now.Label : now.Current });
            var currentIndex = -1;
            for (var i = 0; i < shown.Count; i++)
                if (shown[i].Time <= now.Position) currentIndex = i;
            if (currentIndex < 0 && shown.Count == 1) currentIndex = 0;
            for (var i = 0; i < shown.Count; i++)
            {
                var line = shown[i];
                var current = i == currentIndex;
                Host.Children.Add(Lyric(current && now.Current.Length > 0 ? now.Current : line.Text, current, state));
            }
        }
    }

    // 1.2.0 overlay.js 的 fmt：>= 10 万显示 x.x 万。
    static string FormatCount(int n) =>
        n >= 100000 ? (n / 10000.0).ToString("0.0").TrimEnd('0').TrimEnd('.') + "万" : n.ToString();

    public static Border Card(string name, string text, AppState state) =>
        Card(new ChatLine { Name = name, Text = text }, state);

    // 1.2.0 overlay.js render() 的卡片结构：头像 + 名字 + 粉丝牌 + 房管/舰长徽章
    // + 内容；礼物、醒目留言、上舰各有自己的底色/价格。1.3.1 把所有类型
    // 渲染成完全一样的卡片，Type 根本没被读，头像也只是名字那行文本。
    public static Border Card(ChatLine line, AppState state)
    {
        var ink = DarkText ? Parse("#1A1A1A") : Parse("#FFFFFF");
        if (!state.AdaptiveText) ink = Parse(state.TextColor);
        var font = FontHelper.Family(state.CardFont);
        var nameSize = Math.Max(12, state.FontSize - 2);
        var smallSize = Math.Max(10, state.FontSize - 5);

        var panel = new StackPanel { Spacing = 2 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (state.ShowAvatar)
        {
            // 头像圆：字号 1.6 倍的圆，里面先放名字首字，Face 图盖在上面，
            // 图加载失败就藏掉，首字兜底。只用消息里自带的 Face，不另查接口。
            var size = state.FontSize + 12;
            var initial = string.IsNullOrWhiteSpace(line.Name) ? "?" : line.Name.Trim()[..1];
            var circle = new Grid { Width = size, Height = size };
            var bg = new Border
            {
                Background = new SolidColorBrush(Parse(state.NameColor)),
                CornerRadius = new CornerRadius(size / 2),
                Child = new TextBlock { Text = initial, FontFamily = font, Foreground = new SolidColorBrush(Parse("#FFFFFF")), FontSize = nameSize, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            circle.Children.Add(bg);
            if (Uri.TryCreate(line.Face, UriKind.Absolute, out var faceUri) && (faceUri.Scheme == "http" || faceUri.Scheme == "https"))
            {
                // Ellipse + ImageBrush 才能真正裁成圆；图加载失败就藏掉，首字兜底。
                try
                {
                    var bmp = new BitmapImage(faceUri);
                    var ellipse = new Ellipse { Width = size, Height = size, Fill = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill } };
                    bmp.ImageFailed += (_, _) => ellipse.Visibility = Visibility.Collapsed;
                    circle.Children.Add(ellipse);
                }
                catch { /* 图打不开就留首字 */ }
            }
            head.Children.Add(circle);
        }
        // 1.2.0 里 showAvatar 只控制头像（.av），名字一直显示；
        // 1.3.1 把名字也绑在 ShowAvatar 上，关头像就连名字都没了，这里改回。
        head.Children.Add(new TextBlock { Text = line.Name, FontFamily = font, Foreground = new SolidColorBrush(Parse(state.NameColor)), FontSize = nameSize, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        if (line.MedalLevel > 0 && line.MedalName.Length > 0)
        {
            // 1.2.0 统一样式的粉丝牌：底 #FF5FC8、字 #2A0A1E；B 站自带颜色时用它做底、白字。
            var hasColor = TryParseHex(line.MedalColor, out var medalBg);
            head.Children.Add(new Border
            {
                Background = new SolidColorBrush(hasColor ? medalBg : Parse("#FF5FC8")),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = $"{line.MedalName} lv.{line.MedalLevel}", FontFamily = font, Foreground = new SolidColorBrush(hasColor ? Parse("#FFFFFF") : Parse("#2A0A1E")), FontSize = smallSize, FontWeight = FontWeights.Bold }
            });
        }
        if (line.GuardLevel is >= 1 and <= 3)
        {
            // 1.2.0 overlay.js 的 GUARD = ['', '总督', '提督', '舰长']。
            var guardName = line.GuardLevel switch { 1 => "总督", 2 => "提督", _ => "舰长" };
            head.Children.Add(new Border
            {
                Background = new SolidColorBrush(Parse("#7C4DFF")),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = guardName, FontFamily = font, Foreground = new SolidColorBrush(Parse("#FFFFFF")), FontSize = smallSize, FontWeight = FontWeights.Bold }
            });
        }
        if (line.Admin)
            head.Children.Add(new Border
            {
                Background = new SolidColorBrush(Parse("#FF8A65")),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "管", FontFamily = font, Foreground = new SolidColorBrush(Parse("#FFFFFF")), FontSize = smallSize, FontWeight = FontWeights.Bold }
            });
        if (line.Type == "superchat" && line.Price > 0)
            head.Children.Add(new TextBlock { Text = $"¥{line.Price}", FontFamily = font, Foreground = new SolidColorBrush(ink), FontSize = nameSize, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock { Text = line.Text, FontFamily = font, Foreground = new SolidColorBrush(ink), FontSize = state.FontSize, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        if (line.Type == "danmaku" && Uri.TryCreate(line.Emote, UriKind.Absolute, out var emoteUri) && (emoteUri.Scheme == "http" || emoteUri.Scheme == "https"))
        {
            // 1.2.0 表情图高 3.6em、最宽 9em。加载失败就藏掉，不留破图。
            var emoteImg = new Image { Height = state.FontSize * 3, MaxWidth = state.FontSize * 9, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            emoteImg.ImageFailed += (_, _) => emoteImg.Visibility = Visibility.Collapsed;
            try { emoteImg.Source = new BitmapImage(emoteUri); panel.Children.Add(emoteImg); } catch { }
        }
        // 醒目留言用 B 站自己的底色（background_bottom_color），没有就用卡片色。
        var color = line.Type == "superchat" && TryParseHex(line.Color, out var scColor) ? scColor : Parse(state.CardColor);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb((byte)(state.CardOpacity * 255), color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(state.Corner),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    static bool TryParseHex(string? hex, out Color color)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var n))
        {
            color = Color.FromArgb(255, (byte)(n >> 16), (byte)(n >> 8), (byte)n);
            return true;
        }
        color = default;
        return false;
    }

    static Border Lyric(string text, bool current, AppState state)
    {
        var align = state.LyricAlign == "left" ? TextAlignment.Left : state.LyricAlign == "right" ? TextAlignment.Right : TextAlignment.Center;
        var hAlign = state.LyricAlign == "left" ? HorizontalAlignment.Left : state.LyricAlign == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        var fill = new SolidColorBrush(Parse(current ? state.LyricHighlight : state.LyricColor));
        var stack = new Grid();
        TextBlock Make(Color color, Thickness margin) => new()
        {
            Text = text,
            FontFamily = FontHelper.Family(state.LyricFont),
            FontSize = current ? state.LyricSize : Math.Max(14, state.LyricSize - 8),
            Foreground = new SolidColorBrush(color),
            TextAlignment = align,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = hAlign,
            Margin = margin
        };
        if (state.LyricOutline)
        {
            var edge = Parse("#000000");
            foreach (var shift in new[] { new Thickness(1, 0, 0, 0), new Thickness(-1, 0, 0, 0), new Thickness(0, 1, 0, 0), new Thickness(0, -1, 0, 0) })
                stack.Children.Add(Make(edge, shift));
        }
        else if (state.LyricShadow)
            stack.Children.Add(Make(Color.FromArgb(180, 0, 0, 0), new Thickness(2, 2, 0, 0)));
        stack.Children.Add(Make(fill.Color, new Thickness(0)));
        var bg = Color.FromArgb((byte)(Math.Clamp(state.LyricBg, 0, 1) * 255), 0, 0, 0);
        return new Border { Background = new SolidColorBrush(bg), Padding = new Thickness(12, 4, 12, 4), Child = stack, HorizontalAlignment = HorizontalAlignment.Stretch };
    }

    public static Color Parse(string hex)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var n))
            return Color.FromArgb(255, 26, 26, 26);
        return Color.FromArgb(255, (byte)(n >> 16), (byte)(n >> 8), (byte)n);
    }
}
