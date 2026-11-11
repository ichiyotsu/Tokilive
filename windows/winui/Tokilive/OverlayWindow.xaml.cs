using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
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
    // #25 淡出：上一轮还在的卡片（行 → 卡片），以及正在淡出的卡片。
    // 淡出中的卡片留在 Host 原位播完才移除，所以 Render() 不能再 Children.Clear()。
    readonly Dictionary<ChatLine, Border> _liveCards = new();
    readonly List<Border> _fadingCards = new();
    // blur 入场：卡片 → 真模糊覆盖层（表面 / 效果刷 / 精灵视觉），播完即撤。
    readonly Dictionary<Border, (CompositionVisualSurface Surface, CompositionEffectBrush Brush, SpriteVisual Sprite)> _blurOverlays = new();

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
        var state = AppState.Current;
        var now = DateTimeOffset.UtcNow;
        if (_kind == Kind.Danmaku)
        {
            // 逐条过期：LiveService.LifetimeOf 用 DurationSeconds（醒目留言按
            // B 站 time 字段延长，取 max(30 秒, duration)）；MainWindow 的
            // 1 秒过期定时器（HasExpired，同一条规则）会推着 Render 重绘。
            // LifetimeOf 可能返回 PositiveInfinity（DanmakuLifetime=0，不消失）：
            // 不能 TimeSpan.FromSeconds(infinity)，会抛 OverflowException，
            // 所以这里直接比秒数。
            var lines = LiveService.Lines.Where(l => (now - l.At).TotalSeconds <= LiveService.LifetimeOf(l)).TakeLast(Math.Max(1, state.MaxMessages)).ToList();
            // #25：消失的行（过期、被顶掉）先摘出来播淡出，淡出中的卡片
            // 留在 Host 原位播完才移除；其余旧卡片清掉重建。
            BeginFadeOutFor(_liveCards.Keys.Where(l => !lines.Contains(l)).ToList());
            SweepNonFading();
            if (lines.Count == 0 && _fadingCards.Count == 0) Host.Children.Add(Card("等待弹幕", "连接后会显示在这里", state));
            // 已经不在列表里的行（过期、被顶掉）从已播集合里清掉，
            // 免得集合一直长。只给第一次渲染的新行播入场动画。
            _animated.RemoveWhere(l => !lines.Contains(l));
            foreach (var line in lines)
            {
                var card = Card(line, state);
                _liveCards[line] = card;
                if (_animated.Add(line)) AnimateCard(card, line, state);
                Host.Children.Add(card);
            }
        }
        else if (_kind == Kind.Live)
        {
            // 1.2.0 的实时窗互动（进场/关注/点赞/分享）6 秒后淡出、最多 5 条。
            // 这里按 LiveService.PillLifetime（6 秒，和 HasExpired 同一条规则）过滤，
            // 消失的 pill 和弹幕一样播 #25 的淡出。
            var pills = state.MiniPills
                ? LiveService.Pills.Where(l => now - l.At <= LiveService.PillLifetime).TakeLast(Math.Max(1, state.MiniMax)).ToList()
                : new List<ChatLine>();
            BeginFadeOutFor(_liveCards.Keys.Where(l => !pills.Contains(l)).ToList());
            SweepNonFading();
            // 1.2.0 的 miniStats 关掉就不画数据栏；miniLayout 'v' 是竖排数据栏，
            // 'auto' 按窗口宽高比（宽/高 > 1.8 算横条，和 1.2.0 overlay.js 的 layout() 一样）。
            if (state.MiniStats)
            {
                var vertical = state.MiniLayout == "v" || (state.MiniLayout == "auto" && AppWindow.Size.Height > 0 && AppWindow.Size.Width / (double)AppWindow.Size.Height <= 1.8);
                var statsText = vertical
                    ? $"点赞 {FormatCount(LiveService.Likes)}\n弹幕 {FormatCount(LiveService.Danmaku)}\n在线 {FormatCount(LiveService.Online)}\n{LiveService.Watched}"
                    : $"点赞 {FormatCount(LiveService.Likes)}   弹幕 {FormatCount(LiveService.Danmaku)}   在线 {FormatCount(LiveService.Online)}\n{LiveService.Watched}";
                Host.Children.Insert(0, Card("实时", statsText, state));
            }
            if (state.MiniPills && pills.Count == 0 && _fadingCards.Count == 0) Host.Children.Add(Card("互动", "进场、关注、点赞和分享出现在这里", state));
            foreach (var line in pills)
            {
                var card = Card(line, state);
                _liveCards[line] = card;
                Host.Children.Add(card);
            }
        }
        else
        {
            Host.Children.Clear();
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

    // #25 淡出：消失的行播 0.8 秒淡出（透明度 →0、左移 24px、缩到 0.92，
    // 和 1.2.0 的 .msg.out 一致），播完才从 Host 里移除。
    // 淡出中的卡片不参与 SweepNonFading 的清理；播完靠 Storyboard.Completed
    // 自己摘掉，窗口关掉时实例跟着销毁，不留后台计时器。
    void BeginFadeOutFor(List<ChatLine> gone)
    {
        foreach (var line in gone)
        {
            if (!_liveCards.TryGetValue(line, out var card)) continue;
            _liveCards.Remove(line);
            _animated.Remove(line);
            if (_fadingCards.Contains(card)) continue;
            _fadingCards.Add(card);
            FadeOutCard(card);
        }
    }

    void SweepNonFading()
    {
        for (var i = Host.Children.Count - 1; i >= 0; i--)
            if (Host.Children[i] is Border b && _fadingCards.Contains(b)) continue;
            else Host.Children.RemoveAt(i);
    }

    void FadeOutCard(Border card)
    {
        // 入场动画可能还在改 RenderTransform/Opacity，淡出用新的
        // CompositeTransform 接管，并从当前透明度起播，避免闪一下。
        card.RenderTransform = new CompositeTransform();
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        var dur = new Duration(TimeSpan.FromMilliseconds(800));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var sb = new Storyboard();
        var fade = new DoubleAnimation { From = card.Opacity, To = 0, Duration = dur };
        Storyboard.SetTarget(fade, card);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);
        var slide = new DoubleAnimation { From = 0, To = -24, Duration = dur, EasingFunction = ease };
        Storyboard.SetTarget(slide, card);
        Storyboard.SetTargetProperty(slide, "(UIElement.RenderTransform).(CompositeTransform.TranslateX)");
        sb.Children.Add(slide);
        foreach (var prop in new[] { "(UIElement.RenderTransform).(CompositeTransform.ScaleX)", "(UIElement.RenderTransform).(CompositeTransform.ScaleY)" })
        {
            var shrink = new DoubleAnimation { From = 1, To = 0.92, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(shrink, card);
            Storyboard.SetTargetProperty(shrink, prop);
            sb.Children.Add(shrink);
        }
        sb.Completed += (_, _) =>
        {
            _fadingCards.Remove(card);
            Host.Children.Remove(card);
        };
        sb.Begin();
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
                // 真模糊在 StartRealBlur 里（Win2D GaussianBlurEffect），
                // 这里只把透明度起手设 0，模糊层和淡入都在 Loaded 里起。
                card.Opacity = 0;
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
        card.Loaded += (_, _) =>
        {
            if (preset == "blur") StartRealBlur(card, duration);
            PlayCardStoryboard(card, preset, duration, preset == "blur" ? () => RemoveBlur(card) : null);
        };
    }

    // blur 入场：真模糊，不是以前的淡入 + 缩放模拟。
    // 做法是 CommunityToolkit 的 Blur 用的同一套路：CompositionVisualSurface
    // 抓卡片视觉树 → Win2D GaussianBlurEffect 做效果刷 → SpriteVisual 以子视觉
    // 盖在卡片上面，BlurAmount 从 12 动画到 0；盖住的那层本身就是卡片内容
    // 的拷贝（带底色，不透明度 0.86 左右），所以下面那份原内容基本不会重影，
    // 播完（淡入 Storyboard 的 Completed）就撤掉子视觉，还原成普通卡片。
    // 抓表面失败（比如透明窗上拿不到表面）时只留淡入，不抛异常；
    // 卡片在播完前被清掉时靠 Unloaded 摘 overlay，不让 _blurOverlays 一直
    // 捏着已离树的卡片。真机上到底糊不糊、重影严不严重，必须在 Windows 上看。
    void StartRealBlur(Border card, TimeSpan duration)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(card);
            var compositor = visual.Compositor;
            var w = (float)Math.Max(1, card.ActualWidth);
            var h = (float)Math.Max(1, card.ActualHeight);
            var surface = compositor.CreateVisualSurface();
            surface.SourceVisual = visual;
            surface.SourceSize = new Vector2(w, h);
            try
            {
                // 卡片被 TightenCard 收窄后尺寸会变，表面跟着变，
                // 不然模糊层会被拉伸；这一步动不了就用创建时的一次尺寸。
                var surfaceSize = compositor.CreateExpressionAnimation("source.Size");
                surfaceSize.SetReferenceParameter("source", visual);
                surface.StartAnimation("SourceSize", surfaceSize);
            }
            catch { /* 表面尺寸跟不上就用创建时的尺寸 */ }
            var surfaceBrush = compositor.CreateSurfaceBrush(surface);
            surfaceBrush.Stretch = CompositionStretch.Fill;
            var factory = compositor.CreateEffectFactory(
                new GaussianBlurEffect
                {
                    Name = "Blur",
                    Source = new CompositionEffectSourceParameter("source"),
                    BlurAmount = 12f,
                    BorderMode = EffectBorderMode.Hard,
                },
                ["Blur.BlurAmount"]);
            var brush = factory.CreateBrush();
            brush.SetSourceParameter("source", surfaceBrush);
            var sprite = compositor.CreateSpriteVisual();
            sprite.Brush = brush;
            sprite.Size = new Vector2(w, h);
            var spriteSize = compositor.CreateExpressionAnimation("source.Size");
            spriteSize.SetReferenceParameter("source", visual);
            sprite.StartAnimation("Size", spriteSize);
            var blurAnim = compositor.CreateScalarKeyFrameAnimation();
            blurAnim.InsertKeyFrame(0f, 12f);
            blurAnim.InsertKeyFrame(1f, 0f);
            blurAnim.Duration = duration;
            brush.StartAnimation("Blur.BlurAmount", blurAnim);
            // 上面任何一步抛异常都还没挂子视觉，直接退回淡入；挂上之后再记。
            ElementCompositionPreview.SetElementChildVisual(card, sprite);
            _blurOverlays[card] = (surface, brush, sprite);
            card.Unloaded += (_, _) => RemoveBlur(card);
        }
        catch { /* Composition 失败就只淡入，见 AnimateCard 的注释 */ }
    }

    void RemoveBlur(Border card)
    {
        try
        {
            ElementCompositionPreview.SetElementChildVisual(card, null);
            if (!_blurOverlays.TryGetValue(card, out var overlay)) return;
            _blurOverlays.Remove(card);
            overlay.Brush.Dispose();
            overlay.Sprite.Dispose();
            overlay.Surface.Dispose();
        }
        catch { /* 窗口已关之类，清理失败不用管 */ }
    }

    static void PlayCardStoryboard(Border card, string preset, TimeSpan duration, Action? onCompleted = null)
    {
        var sb = new Storyboard();
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(duration) };
        Storyboard.SetTarget(fade, card);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);
        switch (preset)
        {
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
            // blur：只有淡入，模糊由 StartRealBlur 的 Composition 动画负责。
        }
        if (onCompleted != null) sb.Completed += (_, _) => onCompleted();
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
    // TextShadow>0 时正文包了一层 Grid（阴影在下、前景在上）：打字只动前景，
    // 阴影层先藏起来，免得整句提前从阴影里透出来，打完再恢复。
    static void TypeCard(Border card, double baseMs, double speed)
    {
        if (card.Child is not StackPanel panel || panel.Children.Count < 2) return;
        UIElement? shadowLayer = null;
        var content = panel.Children[1];
        if (content is Grid grid && grid.Children.Count == 2)
        {
            shadowLayer = grid.Children[0];
            content = grid.Children[1];
        }
        var segments = new List<(Run Run, List<Rune> Runes)>();
        if (content is TextBlock tb)
        {
            var text = tb.Text ?? "";
            if (text.Length == 0) return;
            tb.Text = "";
            var run = new Run();
            tb.Inlines.Add(run);
            segments.Add((run, text.EnumerateRunes().ToList()));
        }
        else if (content is RichTextBlock rich
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
        DispatcherTimer? timer = null;
        void Finish()
        {
            timer?.Stop();
            if (shadowLayer != null) shadowLayer.Visibility = Visibility.Visible;
        }
        if (shadowLayer != null) shadowLayer.Visibility = Visibility.Collapsed;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        var seg = 0;
        var pos = 0;
        timer.Tick += (_, _) =>
        {
            if (seg >= segments.Count) { Finish(); return; }
            var (run, runes) = segments[seg];
            run.Text += runes[pos].ToString();
            if (++pos >= runes.Count) { seg++; pos = 0; }
            if (seg >= segments.Count) Finish();
        };
        card.Unloaded += (_, _) => Finish();
        timer.Start();
    }

    // 1.2.0 overlay.js 的 fmt：>= 10 万显示 x.x 万。
    static string FormatCount(int n) =>
        n >= 100000 ? (n / 10000.0).ToString("0.0").TrimEnd('0').TrimEnd('.') + "万" : n.ToString();

    public static Border Card(string name, string text, AppState state) =>
        Card(new ChatLine { Name = name, Text = text }, state);

    // #3 字重：DanmakuFontWeight 是 "400"/"500"/"600"/"700"/"800" 这样的
    // 数字串（也接受 light/normal/bold 这类词）；未知值按默认 700（Bold）处理。
    static Windows.UI.Text.FontWeight WeightOf(string weight) => (weight ?? "").Trim().ToLowerInvariant() switch
    {
        "light" or "300" => FontWeights.Light,
        "400" or "normal" => FontWeights.Normal,
        "500" or "medium" => FontWeights.Medium,
        "600" or "semibold" => FontWeights.SemiBold,
        "800" or "extrabold" => FontWeights.ExtraBold,
        "900" or "black" => FontWeights.Black,
        _ => FontWeights.Bold,
    };

    // #6 卡片主题：system 跟随系统；light/dark 强制；transparent 另外处理
    //（IsCardDark 对它返回 false，文字色走浅色那支）。
    // 注意 Application.Current.RequestedTheme 是 ApplicationTheme，
    // 不是 ElementTheme，别按任务原文写成 ElementTheme.Dark。
    static bool IsCardDark(AppState state) => state.CardThemeMode?.Trim().ToLowerInvariant() switch
    {
        "dark" => true,
        "light" or "transparent" => false,
        _ => Application.Current?.RequestedTheme == ApplicationTheme.Dark,
    };

    // #15/#27 胶囊圆角：Corner==0 时 chips 用 999——WinUI 里圆角超过
    // 元素半高就按胶囊画，999 对小 chips 来说就是精确的胶囊。
    // 卡片本身在 Loaded 里按实测半高写精确值（见 Card() 末尾）。
    static CornerRadius ChipRadius(AppState state) => new(state.Corner == 0 ? 999 : state.Corner);

    // 按比例往黑里混（近似 1.2.0 的 color-mix(in srgb, c 45%, #000)，
    // 粉丝牌 bilibili 样式的等级块底色用）。
    static Color Darken(Color c, double factor) =>
        Color.FromArgb(255, (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    // #4 文字阴影：前景之下放一份黑色偏移副本（和 Lyric() 的分层做法一样），
    // 偏移 (2,2)，透明度 = TextShadow；shadow<=0 时直接返回前景，不包 Grid。
    static UIElement ShadowText(TextBlock foreground, double shadow)
    {
        if (shadow <= 0) return foreground;
        var alpha = (byte)Math.Clamp(shadow * 255, 0, 255);
        var ghost = new TextBlock
        {
            Text = foreground.Text,
            FontFamily = foreground.FontFamily,
            FontSize = foreground.FontSize,
            FontWeight = foreground.FontWeight,
            Foreground = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)),
            TextWrapping = foreground.TextWrapping,
            Margin = new Thickness(2, 2, 0, 0)
        };
        return new Grid { Children = { ghost, foreground } };
    }

    // #23 收紧宽度：按 1.2.0 tighten() 的思路，进树后用无限宽量一次 head +
    // content 的自然宽度，卡片缩到内容宽 + 左右内边距（只缩不扩，不超过当前全宽）。
    // 安全子集：量的是不换行的单行宽，长消息量出来比窗口还宽，取 min 后等于没缩；
    // 1.2.0 是按折行后的每行 rect 取最宽，WinUI 里拿不到行级 rect，只能做到这一步。
    // 量不到（ActualWidth 为 0）或抛异常时保持全宽，不挡渲染。
    static void TightenCard(Border card)
    {
        try
        {
            if (card.Child is not StackPanel panel) return;
            var full = card.ActualWidth;
            if (full <= 0) return;
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var target = Math.Ceiling(panel.DesiredSize.Width + 24 + 1);
            if (target < full - 1)
            {
                card.Width = target;
                // 1.2.0 的卡片是 align-self:flex-start（左对齐），不是拉满。
                card.HorizontalAlignment = HorizontalAlignment.Left;
            }
        }
        catch { /* 量不出来就保持全宽 */ }
    }

    // 1.2.0 overlay.js render() 的卡片结构：头像 + 名字 + 粉丝牌 + 房管/舰长徽章
    // + 内容；礼物、醒目留言、上舰各有自己的底色/价格。1.3.1 把所有类型
    // 渲染成完全一样的卡片，Type 根本没被读，头像也只是名字那行文本。
    public static Border Card(ChatLine line, AppState state)
    {
        var weight = WeightOf(state.DanmakuFontWeight);
        var dark = IsCardDark(state);
        var transparent = string.Equals(state.CardThemeMode, "transparent", StringComparison.OrdinalIgnoreCase);
        // #7 调色板：弹幕卡片继续用用户自己的 CardColor/TextColor/NameColor
        //（那就是弹幕的 overrides）；礼物/上舰/进场/关注/点赞/分享走种子色
        // 调色板，醒目留言也走调色板、除非 ScBiliColor 开了才用 B 站底色。
        // follow/like/share 在 PaletteHelper 里没有自己的 key，用 enter 那套。
        PaletteHelper.PaletteRoles? roles = line.Type switch
        {
            "gift" => PaletteHelper.For("gift", state.SeedColor, dark),
            "guard" => PaletteHelper.For("guard", state.SeedColor, dark),
            "superchat" => PaletteHelper.For("superchat", state.SeedColor, dark),
            "enter" or "follow" or "like" or "share" => PaletteHelper.For("enter", state.SeedColor, dark),
            _ => null
        };
        var nameColorHex = roles?.Name ?? state.NameColor;
        // #6 文字色：非自适应时用户 TextColor 优先；自适应时深色卡片配白字，
        // 浅色沿用原来的 DarkText 开关（深字 #1A1A1A / 白字）。
        // 调色板卡片不走这两支，直接用角色里的 Text。
        var ink = roles != null ? Parse(roles.Text)
            : !state.AdaptiveText ? Parse(state.TextColor)
            : dark ? Parse("#FFFFFF")
            : DarkText ? Parse("#1A1A1A") : Parse("#FFFFFF");
        // #9 三路字体栈：CardFont → CjkFont → JpFont → 系统回退。
        var font = FontHelper.FamilyForCards(state.CardFont, state.CjkFont, state.JpFont);
        var nameSize = Math.Max(12, state.FontSize - 2);
        var smallSize = Math.Max(10, state.FontSize - 5);
        var chipRadius = ChipRadius(state);
        var shadow = state.TextShadow;

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
                Background = new SolidColorBrush(Parse(nameColorHex)),
                CornerRadius = new CornerRadius(size / 2),
                Child = new TextBlock { Text = initial, FontFamily = font, Foreground = new SolidColorBrush(Parse("#FFFFFF")), FontSize = nameSize, FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
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
        // #4：名字也带文字阴影（TextShadow>0 时包一层 Grid，不影响 head 布局）。
        var nameTb = new TextBlock { Text = line.Name, FontFamily = font, Foreground = new SolidColorBrush(Parse(nameColorHex)), FontSize = nameSize, FontWeight = weight, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(ShadowText(nameTb, shadow));
        if (state.ShowMedal && line.MedalLevel > 0 && line.MedalName.Length > 0)
        {
            // #28 粉丝牌：默认 unified 用 1.2.0 的固定配色（bg #FF5FC8、字 #2A0A1E、
            // 等级块底 #4A0A30、白字），和 B 站自带颜色无关；MedalStyle 选 bilibili
            // 时才用消息自带的颜色做底、白字，等级块底是它往黑里混 45% 的版本。
            // 结构也照 1.2.0：外层是名字、内层是 lv.N 的小块。
            Color medalBg, medalFg, lvBg, lvFg;
            if (string.Equals(state.MedalStyle, "bilibili", StringComparison.OrdinalIgnoreCase)
                && TryParseHex(line.MedalColor, out var biliBg))
            {
                medalBg = biliBg;
                medalFg = Parse("#FFFFFF");
                lvBg = Darken(biliBg, 0.45);
                lvFg = Parse("#FFFFFF");
            }
            else
            {
                medalBg = Parse("#FF5FC8");
                medalFg = Parse("#2A0A1E");
                lvBg = Parse("#4A0A30");
                lvFg = Parse("#FFFFFF");
            }
            var medalInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
            medalInner.Children.Add(new TextBlock { Text = line.MedalName, FontFamily = font, Foreground = new SolidColorBrush(medalFg), FontSize = smallSize, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center });
            medalInner.Children.Add(new Border
            {
                Background = new SolidColorBrush(lvBg),
                CornerRadius = chipRadius,
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = $"lv.{line.MedalLevel}", FontFamily = font, Foreground = new SolidColorBrush(lvFg), FontSize = smallSize, FontWeight = weight }
            });
            head.Children.Add(new Border
            {
                Background = new SolidColorBrush(medalBg),
                CornerRadius = chipRadius,
                Padding = new Thickness(8, 1, 2, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = medalInner
            });
        }
        if (line.GuardLevel is >= 1 and <= 3)
        {
            // 1.2.0 overlay.js 的 GUARD = ['', '总督', '提督', '舰长']。
            var guardName = line.GuardLevel switch { 1 => "总督", 2 => "提督", _ => "舰长" };
            head.Children.Add(new Border
            {
                Background = new SolidColorBrush(Parse("#7C4DFF")),
                CornerRadius = chipRadius,
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = guardName, FontFamily = font, Foreground = new SolidColorBrush(Parse("#FFFFFF")), FontSize = smallSize, FontWeight = weight }
            });
        }
        if (line.Admin)
            head.Children.Add(new Border
            {
                Background = new SolidColorBrush(Parse("#FF8A65")),
                CornerRadius = chipRadius,
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "管", FontFamily = font, Foreground = new SolidColorBrush(Parse("#FFFFFF")), FontSize = smallSize, FontWeight = weight }
            });
        if (line.Type == "superchat" && line.Price > 0)
            head.Children.Add(new TextBlock { Text = $"¥{line.Price}", FontFamily = font, Foreground = new SolidColorBrush(ink), FontSize = nameSize, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        panel.Children.Add(head);
        panel.Children.Add(MessageContent(line, state, font, ink, weight));
        // #6/#28 卡片底色：transparent 模式无填充、只有描边；调色板卡片
        // 用角色里的 Bg（深浅已经在 Build(seed, dark) 里算好）；弹幕卡片
        // 深色模式用 #2B2B2B、浅色用用户 CardColor；醒目留言只有 ScBiliColor
        // 开时才用 B 站的 background_bottom_color，名字和正文仍走调色板角色。
        Color baseColor;
        if (roles != null)
        {
            if (line.Type == "superchat" && state.ScBiliColor && TryParseHex(line.Color, out var scColor)) baseColor = scColor;
            else baseColor = Parse(roles.Bg);
        }
        else if (dark) baseColor = Parse("#2B2B2B");
        else baseColor = Parse(state.CardColor);
        Brush? background;
        if (transparent) background = null;
        else if (state.CardBlur > 0)
            // #5 磨砂：AcrylicBrush 采样窗后内容做系统级磨砂（Backdrop 模式），
            // TintOpacity 跟 CardOpacity；AcrylicBrush 没有模糊半径属性，
            // CardBlur 的 px 数值映射不到它上面，>0 只做开关，==0 走实色。
            // 透明悬浮窗上 Acrylic 到底出不出来，必须在 Windows 上验证。
            background = new AcrylicBrush { TintColor = baseColor, TintOpacity = Math.Clamp(state.CardOpacity, 0, 1), FallbackColor = baseColor, BackgroundSource = AcrylicBackgroundSource.Backdrop };
        else
            background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(state.CardOpacity, 0, 1) * 255), baseColor.R, baseColor.G, baseColor.B));
        var card = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(state.Corner == 0 ? 999 : state.Corner),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // transparent 模式：无填充，只有描边（文字靠 #4 的阴影才看得清）。
            BorderBrush = transparent ? new SolidColorBrush(Color.FromArgb(140, ink.R, ink.G, ink.B)) : null,
            BorderThickness = transparent ? new Thickness(1) : new Thickness(0),
        };
        // #15/#23：进树后量一次——收紧到内容宽（只缩不扩）；Corner==0 时
        // 先按 999 画胶囊，等宽度定下来、尺寸变化触发 SizeChanged 时
        // 再按实测半高写精确值；宽度没变就一直是 999，视觉上同样是胶囊。
        card.Loaded += (_, _) =>
        {
            TightenCard(card);
            if (state.Corner == 0)
            {
                void OnSized(object _, SizeChangedEventArgs e)
                {
                    card.SizeChanged -= OnSized;
                    if (card.ActualHeight > 0) card.CornerRadius = new CornerRadius(card.ActualHeight / 2);
                }
                card.SizeChanged += OnSized;
            }
        };
        return card;
    }

    // B 站表情：行内显示，和字一样大（高 = 字号，宽按原图比例自动，
    // Stretch.Uniform 不拉变形）。以前是高 3 倍的大贴纸贴在文字下面，
    // "[xxx]" 那段文字还留在上面，现在图直接顶掉 token 的位置。
    // 整条消息就是一个表情（"[dog]"）时只显示图；表情夹在文字中间时，
    // 把文字在 token 处切开，图插进去。图加载失败就把 "[xxx]" 文字放
    // 回去（和 1.2.0 的 onerror 回退一个意思），不留破图。
    // 只有弹幕带表情（LiveService 从 DANMU_MSG 的 i[0][13].url 取），
    // 其他类型不解析表情；一条消息最多一个表情图。
    static UIElement MessageContent(ChatLine line, AppState state, FontFamily font, Color ink, Windows.UI.Text.FontWeight weight)
    {
        var text = line.Text ?? "";
        var plain = new TextBlock { Text = text, FontFamily = font, Foreground = new SolidColorBrush(ink), FontSize = state.FontSize, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
        if (line.Type != "danmaku"
            || !Uri.TryCreate(line.Emote, UriKind.Absolute, out var emoteUri)
            || (emoteUri.Scheme != "http" && emoteUri.Scheme != "https"))
            return ShadowText(plain, state.TextShadow);
        var token = Regex.Match(text, @"\[[^\[\]]{1,20}\]");
        if (!token.Success) return ShadowText(plain, state.TextShadow);
        var rich = new RichTextBlock { FontFamily = font, Foreground = new SolidColorBrush(ink), FontSize = state.FontSize, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
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
        catch { return ShadowText(plain, state.TextShadow); }
        para.Inlines.Add(container);
        if (token.Index + token.Length < text.Length) para.Inlines.Add(new Run { Text = text[(token.Index + token.Length)..] });
        rich.Blocks.Add(para);
        if (state.TextShadow <= 0) return rich;
        // #4：表情行的阴影是复制一份纯文字版（表情位置用原 token 占位）
        // 垫在下面，和正文同一折行宽度，行高基本对齐。
        var alpha = (byte)Math.Clamp(state.TextShadow * 255, 0, 255);
        var shadowRich = new RichTextBlock { FontFamily = font, Foreground = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)), FontSize = state.FontSize, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0) };
        var shadowPara = new Paragraph();
        if (token.Index > 0) shadowPara.Inlines.Add(new Run { Text = text[..token.Index] });
        shadowPara.Inlines.Add(new Run { Text = token.Value });
        if (token.Index + token.Length < text.Length) shadowPara.Inlines.Add(new Run { Text = text[(token.Index + token.Length)..] });
        shadowRich.Blocks.Add(shadowPara);
        return new Grid { Children = { shadowRich, rich } };
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
        // #9：歌词也走三路字体栈（LyricFont → CjkFont → JpFont → 系统回退）。
        var font = FontHelper.FamilyForCards(state.LyricFont, state.CjkFont, state.JpFont);
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
