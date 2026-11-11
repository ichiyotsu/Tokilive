using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Tokilive;

public sealed partial class OverlayWindow : Window
{
    public enum Kind { Danmaku, Live, Lyrics }
    readonly Kind _kind;
    public static bool DarkText = true;
    // 已经播过入场动画的弹幕行。ChatLine 没有重写 Equals/GetHashCode，
    // HashSet 默认就是引用相等，正好按“同一条消息对象”去重。
    // Render() 每次都会重建全部卡片，不记这一笔的话，每来一条新弹幕、
    // 每次过期重绘，旧卡片都会再播一遍动画。
    readonly HashSet<ChatLine> _animated = new();

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
            // 已经不在列表里的行（过期、被顶掉）从已播集合里清掉，
            // 免得集合一直长。只给第一次渲染的新行播入场动画。
            _animated.RemoveWhere(l => !lines.Contains(l));
            foreach (var line in lines)
            {
                var card = Card(line, state);
                if (_animated.Add(line)) AnimateCard(card, line, state);
                Host.Children.Add(card);
            }
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
            // 这里不用 now：方法顶部已经有个 var now = DateTimeOffset.UtcNow，
            // 嵌套块里再声明同名局部变量会被编译器拒掉。
            var playing = LyricsService.Current;
            var lines = playing.Lines;
            if (lines.Count == 0)
            {
                Host.Children.Add(Lyric(string.IsNullOrEmpty(playing.Current) ? playing.Label : playing.Current, false, state));
                return;
            }
            // 当前行 = Time <= 播放位置的最后一行（和 LyricsService.LineAt
            // 同一个规则），先在全曲里定下标，再围着它取要显示的行。
            // 以前是 TakeLast(LyricLines) 直接取全曲最后几行：行数 >1 时
            // 显示的是歌的结尾，而不是当前行附近。
            var currentIndex = -1;
            for (var i = 0; i < lines.Count; i++)
                if (lines[i].Time <= playing.Position) currentIndex = i; else break;
            // 还没到第一行：显示第一行，不高亮。
            if (currentIndex < 0) currentIndex = 0;
            var currentLine = lines[currentIndex];
            var shown = new List<(string Text, bool Current)>
            {
                (playing.Current.Length > 0 ? playing.Current : currentLine.Text, lines[currentIndex].Time <= playing.Position)
            };
            if (state.LyricMode == "double")
            {
                // 双行：第二行优先显示当前行的翻译（LRC 里用 | 分隔），
                // 没有翻译就显示下一行；都没有就只留一行。
                var second = currentLine.Translation;
                if (string.IsNullOrWhiteSpace(second) && currentIndex + 1 < lines.Count)
                    second = lines[currentIndex + 1].Text;
                if (!string.IsNullOrWhiteSpace(second)) shown.Add((second, false));
            }
            if (state.LyricDirection == "vertical")
            {
                // 竖排：每行是一列字，几列横着排在一起放进 Host，
                // 而不是上下叠两条竖列。当前行在左。
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (var (text, current) in shown) row.Children.Add(Lyric(text, current, state));
                Host.Children.Add(row);
            }
            else
            {
                foreach (var (text, current) in shown) Host.Children.Add(Lyric(text, current, state));
            }
        }
    }

    // 弹幕入场动画。只在 OverlayWindow.Render() 里调：PreviewList 用的
    // Card() 本身不带动画，设置页预览里的卡片永远不播；实时窗/歌词窗
    // 也不走这里。关掉开关或选默认时直接返回，新弹幕直接出现，和现在一样。
    // 时长：基础 350ms（缓慢上升 600ms），实际时长 = 基础 / 速度倍率，
    // 速度 0.5x 时 700ms（上升 1200ms），2x 时 175ms（上升 300ms）。
    void AnimateCard(Border card, ChatLine line, AppState state)
    {
        if (!state.DanmakuAnimEnabled) return;
        var preset = state.DanmakuAnim;
        if (preset.Length == 0 || preset == "default") return;
        var speed = Math.Clamp(state.DanmakuAnimSpeed, 0.5, 2);
        var baseMs = preset == "rise" ? 600.0 : 350.0;
        var duration = TimeSpan.FromMilliseconds(baseMs / speed);
        if (preset == "type")
        {
            TypeCard(card, baseMs, speed);
            return;
        }
        // 先把起始状态设好再进树，避免 Loaded 前闪一下最终态；
        // 动画在 Loaded 里起，卡片进树后立刻触发。
        switch (preset)
        {
            case "blur":
                // WinUI 3 没有不依赖 Win2D 的内容模糊（GaussianBlurEffect
                // 在 Microsoft.Graphics.Canvas.Effects 里），这里不引新包，
                // 用淡入 + 1.04 倍轻微缩放模拟“由虚到实”的柔焦感。
                card.Opacity = 0;
                card.RenderTransform = new ScaleTransform { ScaleX = 1.04, ScaleY = 1.04 };
                card.RenderTransformOrigin = new Point(0.5, 0.5);
                break;
            case "pop":
                card.Opacity = 0;
                card.RenderTransform = new ScaleTransform { ScaleX = 0.8, ScaleY = 0.8 };
                card.RenderTransformOrigin = new Point(0.5, 0.5);
                break;
            case "rise":
                card.Opacity = 0;
                card.RenderTransform = new CompositeTransform { TranslateY = 24 };
                break;
            default:
                return;
        }
        card.Loaded += (_, _) => PlayCardStoryboard(card, preset, duration);
    }

    static void PlayCardStoryboard(Border card, string preset, TimeSpan duration)
    {
        var sb = new Storyboard();
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(duration) };
        Storyboard.SetTarget(fade, card);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);
        switch (preset)
        {
            case "blur":
                AddScale(sb, card, 1.04, 1, duration, new CubicEase { EasingMode = EasingMode.EaseOut });
                break;
            case "pop":
                // 回弹：先冲到 1 再轻微回落，BackEase 的 EaseOut 自带这个过冲。
                AddScale(sb, card, 0.8, 1, duration, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 1.5 });
                break;
            case "rise":
                var rise = new DoubleAnimation { From = 24, To = 0, Duration = new Duration(duration), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(rise, card);
                Storyboard.SetTargetProperty(rise, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)");
                sb.Children.Add(rise);
                break;
        }
        sb.Begin();
    }

    static void AddScale(Storyboard sb, Border card, double from, double to, TimeSpan duration, EasingFunctionBase? ease)
    {
        // X 和 Y 各要一个独立的 DoubleAnimation 实例，不能共用。
        foreach (var prop in new[] { "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", "(UIElement.RenderTransform).(ScaleTransform.ScaleY)" })
        {
            var a = new DoubleAnimation { From = from, To = to, Duration = new Duration(duration) };
            if (ease != null) a.EasingFunction = ease;
            Storyboard.SetTarget(a, card);
            Storyboard.SetTargetProperty(a, prop);
            sb.Children.Add(a);
        }
    }

    // 逐字显示：只动消息正文，名字/头像/粉丝牌一开始就全在。
    // 按 Unicode 码点（Rune）逐字放，表情这类代理对不会被切成两半。
    // 有表情图时图一开始就显示，只逐字打文字部分。
    // 卡片被移出（Unloaded）或打完字就停表，不留后台计时器。
    static void TypeCard(Border card, double baseMs, double speed)
    {
        if (card.Child is not StackPanel panel || panel.Children.Count < 2) return;
        var segments = new List<(Run Run, List<Rune> Runes)>();
        if (panel.Children[1] is TextBlock tb)
        {
            var text = tb.Text ?? "";
            if (text.Length == 0) return;
            tb.Text = "";
            var run = new Run();
            tb.Inlines.Add(run);
            segments.Add((run, text.EnumerateRunes().ToList()));
        }
        else if (panel.Children[1] is RichTextBlock rich
            && rich.Blocks.FirstOrDefault() is Paragraph para)
        {
            foreach (var run in para.Inlines.OfType<Run>().ToList())
            {
                var text = run.Text ?? "";
                if (text.Length == 0) continue;
                segments.Add((run, text.EnumerateRunes().ToList()));
                run.Text = "";
            }
            if (segments.Count == 0) return;
        }
        else return;
        var total = segments.Sum(s => s.Runes.Count);
        if (total == 0) return;
        // 总时长 = 基础 / 速度，每字一跳；太长的句子单跳不低于 8ms。
        var interval = Math.Max(8.0, baseMs / speed / total);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        var seg = 0;
        var pos = 0;
        timer.Tick += (_, _) =>
        {
            if (seg >= segments.Count) { timer.Stop(); return; }
            var (run, runes) = segments[seg];
            run.Text += runes[pos].ToString();
            if (++pos >= runes.Count) { seg++; pos = 0; }
            if (seg >= segments.Count) timer.Stop();
        };
        card.Unloaded += (_, _) => timer.Stop();
        timer.Start();
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
        panel.Children.Add(MessageContent(line, state, font, ink));
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

    // B 站表情：行内显示，和字一样大（高 = 字号，宽按原图比例自动，
    // Stretch.Uniform 不拉变形）。以前是高 3 倍的大贴纸贴在文字下面，
    // "[xxx]" 那段文字还留在上面，现在图直接顶掉 token 的位置。
    // 整条消息就是一个表情（"[dog]"）时只显示图；表情夹在文字中间时，
    // 把文字在 token 处切开，图插进去。图加载失败就把 "[xxx]" 文字放
    // 回去（和 1.2.0 的 onerror 回退一个意思），不留破图。
    // 只有弹幕带表情（LiveService 从 DANMU_MSG 的 i[0][13].url 取），
    // 其他类型不解析表情；一条消息最多一个表情图。
    static UIElement MessageContent(ChatLine line, AppState state, FontFamily font, Color ink)
    {
        var text = line.Text ?? "";
        var plain = new TextBlock { Text = text, FontFamily = font, Foreground = new SolidColorBrush(ink), FontSize = state.FontSize, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        if (line.Type != "danmaku"
            || !Uri.TryCreate(line.Emote, UriKind.Absolute, out var emoteUri)
            || (emoteUri.Scheme != "http" && emoteUri.Scheme != "https"))
            return plain;
        var token = Regex.Match(text, @"\[[^\[\]]{1,20}\]");
        if (!token.Success) return plain;
        var rich = new RichTextBlock { FontFamily = font, Foreground = new SolidColorBrush(ink), FontSize = state.FontSize, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        var para = new Paragraph();
        if (token.Index > 0) para.Inlines.Add(new Run { Text = text[..token.Index] });
        var img = new Image { Height = state.FontSize, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        var container = new InlineUIContainer { Child = img };
        img.ImageFailed += (_, _) =>
        {
            var at = para.Inlines.IndexOf(container);
            if (at >= 0)
            {
                para.Inlines.Insert(at, new Run { Text = token.Value });
                para.Inlines.Remove(container);
            }
        };
        try { img.Source = new BitmapImage(emoteUri); }
        catch { return plain; }
        para.Inlines.Add(container);
        if (token.Index + token.Length < text.Length) para.Inlines.Add(new Run { Text = text[(token.Index + token.Length)..] });
        rich.Blocks.Add(para);
        return rich;
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
        var font = FontHelper.Family(state.LyricFont);
        var weight = LyricsDesigns.WeightOf(state.LyricWeight);
        var size = current ? state.LyricSize : Math.Max(14, state.LyricSize - 8);
        var stack = new Grid();
        // 横排是一行 TextBlock；竖排是一列字（每字一行，从上往下）。
        // EnumerateRunes 按 Unicode 码点切，表情这类代理对不会被切成两半。
        UIElement Make(Color color, Thickness margin)
        {
            if (state.LyricDirection == "vertical")
            {
                var column = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, Margin = margin };
                foreach (var rune in text.EnumerateRunes())
                    column.Children.Add(new TextBlock { Text = rune.ToString(), FontFamily = font, FontSize = size, FontWeight = weight, Foreground = new SolidColorBrush(color), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                return column;
            }
            return new TextBlock
            {
                Text = text,
                FontFamily = font,
                FontSize = size,
                FontWeight = weight,
                Foreground = new SolidColorBrush(color),
                TextAlignment = align,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = hAlign,
                Margin = margin
            };
        }
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
