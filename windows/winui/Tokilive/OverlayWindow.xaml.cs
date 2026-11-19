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
    Windows.Graphics.PointInt32? _drag;
    Windows.Foundation.Point _start;
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
        NativeWin.ApplyOverlay(this, title, ClickThrough, resizable: kind != Kind.Lyrics, noActivate: true, perPixelTransparent: true);
        RestoreBounds();
        Closed += (_, _) => { };
        // 1.3.1 的悬浮窗没有标题栏、没有任何拖动处理，穿透一关也挪不动位置。
        // 1.2.0 解锁时整个窗口都是拖拽区。这里在不穿透时点住窗口就拖动。
        // 不用 WM_NCLBUTTONDOWN 的模态移动循环：它会被 KeepOrder 的
        // SetWindowPos 和消息重绘打断，长按拖不动。改用和输入窗一样的手
        // 动拖动：按下记偏移，移动调 AppWindow.Move，松开结束。
        Root.PointerPressed += (_, e) =>
        {
            if (ClickThrough) return;
            _drag = AppWindow.Position;
            _start = e.GetCurrentPoint(Root).Position;
            Root.CapturePointer(e.Pointer);
        };
        Root.PointerMoved += (_, e) =>
        {
            if (_drag == null || ClickThrough) return;
            var p = e.GetCurrentPoint(Root).Position;
            AppWindow.Move(new Windows.Graphics.PointInt32(_drag.Value.X + (int)(p.X - _start.X), _drag.Value.Y + (int)(p.Y - _start.Y)));
        };
        Root.PointerReleased += (_, _) => _drag = null;
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
        NativeWin.ApplyOverlay(this, AppWindow.Title, ClickThrough || locked, resizable: !locked && _kind != Kind.Lyrics, noActivate: true, perPixelTransparent: true);
        Root.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        var border = _kind == Kind.Live ? AppState.Current.ShowLiveBorder : _kind == Kind.Danmaku && AppState.Current.ShowWindowBorder;
        Root.BorderBrush = border ? new SolidColorBrush(Parse(AppState.Current.UseSystemAccent ? NativeWin.SystemAccent() ?? AppState.Current.AccentHex : AppState.Current.AccentHex)) : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        Root.BorderThickness = border ? new Thickness(2) : new Thickness(0);
    }

    void ConfigureHost()
    {
        if (_kind == Kind.Live)
        {
            // 实时窗本身就是数据条：横条铺满窗口，不要再套一张带假头像的弹幕卡。
            Host.VerticalAlignment = VerticalAlignment.Stretch;
            Host.HorizontalAlignment = HorizontalAlignment.Stretch;
            Host.MaxWidth = 4096;
            Host.MinWidth = 0;
            Host.Margin = new Thickness(0);
            Host.Spacing = 4;
        }
        else if (_kind == Kind.Lyrics)
        {
            Host.VerticalAlignment = VerticalAlignment.Center;
            Host.HorizontalAlignment = HorizontalAlignment.Stretch;
            Host.MaxWidth = 4096;
            Host.MinWidth = 0;
            Host.Margin = new Thickness(8, 0, 8, 0);
            Host.Spacing = 4;
        }
        else
        {
            // 弹幕贴底、左对齐，窗口其余部分保持透明，不铺黑底。
            Host.VerticalAlignment = VerticalAlignment.Bottom;
            Host.HorizontalAlignment = HorizontalAlignment.Left;
            Host.MaxWidth = 560;
            Host.MinWidth = 0;
            Host.Margin = new Thickness(8);
            Host.Spacing = 8;
        }
    }

    public void Render()
    {
        var state = AppState.Current;
        var now = DateTimeOffset.UtcNow;
        ConfigureHost();
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
            if (lines.Count == 0 && _fadingCards.Count == 0) Host.Children.Add(EmptyState(state));
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
                Host.Children.Insert(0, StatsBar(state, vertical, compact: state.MiniPills));
            }
            if (state.MiniPills && pills.Count == 0 && _fadingCards.Count == 0) Host.Children.Add(EmptyState(state, "等待互动", "进场、关注、点赞和分享出现在这里"));
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
            var currentIndex = LyricsService.LineIndexAt(lines, playing.Position);
            // 还没到第一行：显示第一行，不高亮。
            if (currentIndex < 0) currentIndex = 0;
            var currentLine = lines[currentIndex];
            var shown = new List<(LyricLine Line, bool Current, bool Words)>
            {
                (currentLine, lines[currentIndex].Time <= playing.Position, true)
            };
            if (state.LyricMode == "double")
            {
                // 双行：第二行优先显示当前行的翻译（LRC 里用 | 分隔），
                // 没有翻译就显示下一行；都没有就只留一行。翻译没有逐字时间。
                if (!string.IsNullOrWhiteSpace(currentLine.Translation))
                    shown.Add((new LyricLine { Text = currentLine.Translation, Time = currentLine.Time }, false, false));
                else if (currentIndex + 1 < lines.Count)
                    shown.Add((lines[currentIndex + 1], false, false));
            }
            UIElement View((LyricLine Line, bool Current, bool Words) item) =>
                LyricView(item.Line, item.Current, state, playing.Position, item.Words);
            if (state.LyricDirection == "vertical")
            {
                // 竖排：每行是一列字，几列横着排在一起放进 Host，
                // 而不是上下叠两条竖列。当前行在左。
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (var item in shown) row.Children.Add(View(item));
                Host.Children.Add(row);
            }
            else
            {
                foreach (var item in shown) Host.Children.Add(View(item));
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

    // 卡片底色：transparent 无填充；CardBlur>0 走 Acrylic（TintOpacity 跟
    // CardOpacity，FallbackColor 是同色带 CardOpacity 透明度——Acrylic 在透明
    // 悬浮窗上失效时退回实色）；否则实色 + CardOpacity。
    // Card()、StatsBar()、EmptyState() 三处共用，磨砂/浅色/深色走同一条路。
    static Brush? CardBackground(AppState state, Color baseColor, bool transparent)
    {
        if (transparent) return null;
        var alpha = (byte)(Math.Clamp(state.CardOpacity, 0, 1) * 255);
        if (state.CardBlur > 0)
            // #5 磨砂：AcrylicBrush 采样窗后内容做系统级磨砂，
            // TintOpacity 跟 CardOpacity；AcrylicBrush 没有模糊半径属性，
            // CardBlur 的 px 数值映射不到它上面，>0 只做开关，==0 走实色。
            // 注：BackgroundSource 是 UWP 版 AcrylicBrush 才有的属性，
            // WinUI 3 版没有，这里用它默认的采样来源。
            // 透明悬浮窗上 Acrylic 到底出不出来，必须在 Windows 上验证。
            return new AcrylicBrush { TintColor = baseColor, TintOpacity = Math.Clamp(state.CardOpacity, 0, 1), FallbackColor = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B) };
        return new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    // 卡片描边：transparent 沿用旧做法（ink 色 140 透明度）；
    // 其余走调色板的 Line 角色——弹幕卡片没有自己的 roles 时取 danmaku 那套。
    // 以前 roles.Line 算出来却从没画过，所有卡片都没有描边。
    static Color CardLineColor(AppState state, bool dark, PaletteHelper.PaletteRoles? roles, bool transparent, Color ink)
    {
        if (transparent) return Color.FromArgb(140, ink.R, ink.G, ink.B);
        var lineHex = roles?.Line ?? PaletteHelper.For("danmaku", state.SeedColor, dark).Line;
        return Parse(lineHex);
    }

    // head 里的元素：头像、名字、粉丝牌、舰长/房管徽章。
    // 两行卡片（head 行）和单行 pill 共用一份，避免两处各写一套。
    // Avatar 为 null 表示不显示头像；Badges 可能为空。
    static (UIElement? Avatar, UIElement Name, UIElement? Medal, List<UIElement> Badges) HeadParts(
        ChatLine line, AppState state, FontFamily font, string nameColorHex,
        Windows.UI.Text.FontWeight weight, double nameSize, double smallSize,
        CornerRadius chipRadius, double shadow)
    {
        UIElement? avatar = null;
        if (state.ShowAvatar)
        {
            // 头像圆：字号 1.6 倍的圆，里面先放名字首字，Face 图盖在上面，
            // 图加载失败就藏掉，首字兜底。只用消息里自带的 Face，不另查接口。
            var size = state.FontSize + 12;
            var initial = string.IsNullOrWhiteSpace(line.Name) ? "?" : line.Name.Trim()[..1];
            var circle = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
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
            avatar = circle;
        }
        // 1.2.0 里 showAvatar 只控制头像（.av），名字一直显示；
        // 1.3.1 把名字也绑在 ShowAvatar 上，关头像就连名字都没了，这里改回。
        // #4：名字也带文字阴影（TextShadow>0 时包一层 Grid，不影响 head 布局）。
        var nameTb = new TextBlock { Text = line.Name, FontFamily = font, Foreground = new SolidColorBrush(Parse(nameColorHex)), FontSize = nameSize, FontWeight = weight, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        UIElement name = ShadowText(nameTb, shadow);
        UIElement? medal = null;
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
            medal = new Border
            {
                Background = new SolidColorBrush(medalBg),
                CornerRadius = chipRadius,
                Padding = new Thickness(8, 1, 2, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = medalInner
            };
        }
        var badges = new List<UIElement>();
        if (line.GuardLevel is >= 1 and <= 3)
        {
            // 1.2.0 overlay.js 的 GUARD = ['', '总督', '提督', '舰长']。
            var guardName = line.GuardLevel switch { 1 => "总督", 2 => "提督", _ => "舰长" };
            badges.Add(new Border
            {
                Background = new SolidColorBrush(Parse("#7C4DFF")),
                CornerRadius = chipRadius,
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = guardName, FontFamily = font, Foreground = new SolidColorBrush(Parse("#FFFFFF")), FontSize = smallSize, FontWeight = weight }
            });
        }
        if (line.Admin)
            badges.Add(new Border
            {
                Background = new SolidColorBrush(Parse("#FF8A65")),
                CornerRadius = chipRadius,
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "管", FontFamily = font, Foreground = new SolidColorBrush(Parse("#FFFFFF")), FontSize = smallSize, FontWeight = weight }
            });
        return (avatar, name, medal, badges);
    }

    // gift/guard/enter/follow/like/share：单行 pill（参考图第 1/5/7 行）。
    // 名字 + 粉丝牌 + 徽章 + 动作文字全在一行：头像/名字/粉丝牌/徽章各占一列
    // Auto，动作文字占 * 列。窄窗下不许竖排碎字（参考图右下角的竖卡就是这个问题）：
    // 全部 NoWrap，动作文字超出用省略号，固定部分超出时由卡片边缘横向裁掉。
    static Grid PillRow(
        (UIElement? Avatar, UIElement Name, UIElement? Medal, List<UIElement> Badges) head,
        ChatLine line, FontFamily font, Color ink,
        Windows.UI.Text.FontWeight weight, double nameSize)
    {
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center, ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (head.Avatar != null) { Grid.SetColumn(head.Avatar, 0); row.Children.Add(head.Avatar); }
        Grid.SetColumn(head.Name, 1);
        row.Children.Add(head.Name);
        if (head.Medal != null) { Grid.SetColumn(head.Medal, 2); row.Children.Add(head.Medal); }
        if (head.Badges.Count > 0)
        {
            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            foreach (var badge in head.Badges) badgeRow.Children.Add(badge);
            Grid.SetColumn(badgeRow, 3);
            row.Children.Add(badgeRow);
        }
        var action = new TextBlock
        {
            Text = line.Text ?? "",
            FontFamily = font,
            Foreground = new SolidColorBrush(ink),
            FontSize = nameSize,
            FontWeight = weight,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(action, 4);
        row.Children.Add(action);
        return row;
    }

    // #23 收紧宽度（pill 版）：和 TightenCard 同一思路，但孩子是 Grid
    //（横向 StackPanel 会让 TypeCard 把名字当正文逐字打，所以 pill 不用它做根）。
    // 只缩不扩；量不到就保持全宽。
    static void TightenGridCard(Border card)
    {
        try
        {
            if (card.Child is not Grid grid) return;
            var full = card.ActualWidth;
            if (full <= 0) return;
            grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var target = Math.Ceiling(grid.DesiredSize.Width + 24 + 1);
            if (target < full - 1)
            {
                card.Width = target;
                // 1.2.0 的卡片是 align-self:flex-start（左对齐），不是拉满。
                card.HorizontalAlignment = HorizontalAlignment.Left;
            }
        }
        catch { /* 量不出来就保持全宽 */ }
    }

    // 卡片外壳：1dp Line 描边（浅色/深色/磨砂都有，磨砂也不例外）；
    // pill 是胶囊（999，WinUI 按半高画）；普通卡片按 state.Corner，
    // Corner==0 时先按 999 画、进树后按实测半高写精确值（沿用旧逻辑）。
    static Border BuildCard(AppState state, UIElement content, Color baseColor, Color lineColor, bool transparent, bool pill)
    {
        var card = new Border
        {
            Background = CardBackground(state, baseColor, transparent),
            BorderBrush = new SolidColorBrush(lineColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(pill || state.Corner == 0 ? 999 : state.Corner),
            Padding = new Thickness(12, 8, 12, 8),
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        // #15/#23：进树后量一次——收紧到内容宽（只缩不扩）；Corner==0 时
        // 先按 999 画胶囊，等宽度定下来、尺寸变化触发 SizeChanged 时
        // 再按实测半高写精确值；宽度没变就一直是 999，视觉上同样是胶囊。
        card.Loaded += (_, _) =>
        {
            TightenCard(card);
            TightenGridCard(card);
            if (state.Corner == 0 && !pill)
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
    // 重构后（参考图三列：浅色/深色/磨砂）：
    // gift/guard/enter/follow/like/share 是单行 pill（名字 + 粉丝牌 + 动作文字
    // 全在一行）；danmaku/superchat 保持两行卡片，醒目留言价格在右上角 "+30"；
    // 三种样式都有 1dp Line 描边，磨砂（CardBlur>0）是 Acrylic 底 + 描边。
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
        var lineColor = CardLineColor(state, dark, roles, transparent, ink);

        var head = HeadParts(line, state, font, nameColorHex, weight, nameSize, smallSize, chipRadius, shadow);
        // gift/guard/enter/follow/like/share：单行 pill（参考图第 1/5/7 行）。
        if (line.Type is "gift" or "guard" or "enter" or "follow" or "like" or "share")
            return BuildCard(state, PillRow(head, line, font, ink, weight, nameSize), baseColor, lineColor, transparent, pill: true);

        var panel = new StackPanel { Spacing = 2 };
        var headRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (head.Avatar != null) headRow.Children.Add(head.Avatar);
        headRow.Children.Add(head.Name);
        if (head.Medal != null) headRow.Children.Add(head.Medal);
        foreach (var badge in head.Badges) headRow.Children.Add(badge);
        // 醒目留言价格：参考图第 2 行，右上角 "+30"（不再是 head 里的 "¥{Price}"）。
        // 价格色走调色板 superchat 的 price（浅色 tone 90 / 深色 tone 20，
        // 见 PaletteHelper 文件头的映射表）。
        UIElement headElement = headRow;
        if (line.Type == "superchat" && line.Price > 0)
        {
            var price = new TextBlock
            {
                Text = $"+{line.Price}",
                FontFamily = font,
                Foreground = new SolidColorBrush(Parse(PaletteHelper.Tone(state.SeedColor, dark ? 20 : 90))),
                FontSize = nameSize,
                FontWeight = weight,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var headGrid = new Grid();
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            headGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(headRow, 0);
            headGrid.Children.Add(headRow);
            Grid.SetColumn(price, 1);
            headGrid.Children.Add(price);
            headElement = headGrid;
        }
        panel.Children.Add(headElement);
        panel.Children.Add(MessageContent(line, state, font, ink, weight));
        return BuildCard(state, panel, baseColor, lineColor, transparent, pill: false);
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
        // 纯表情（整条只有一个表情 token）：参考图第 6 行，高卡片、表情图居中放大。
        // 图加载失败时回退 token 文字，用卡片 ink 色（深底也不能出现黑字）。
        if (token.Index == 0 && token.Length == text.Trim().Length)
        {
            var size = Math.Max(48, state.FontSize * 3);
            var img = new Image { Height = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var box = new Grid { MinHeight = size + 16, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
            box.Children.Add(img);
            img.ImageFailed += (_, _) =>
            {
                box.Children.Clear();
                box.Children.Add(new TextBlock { Text = token.Value, FontFamily = font, Foreground = new SolidColorBrush(ink), FontSize = state.FontSize, FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            };
            try { img.Source = new BitmapImage(emoteUri); }
            catch { return ShadowText(plain, state.TextShadow); }
            return box;
        }
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

    // 1.0.0 overlay.css 的 #stats：横条是胶囊，标签和数字分开，窗口其余部分透明。
    // 不再用一张带假头像「实」的弹幕卡把整扇窗涂满。
    Border StatsBar(AppState state, bool vertical, bool compact)
    {
        var dark = IsCardDark(state);
        var transparent = string.Equals(state.CardThemeMode, "transparent", StringComparison.OrdinalIgnoreCase);
        var frosted = !transparent && state.CardBlur > 0;
        // 三种样式（参考图最后一行）：
        // 浅色：深色 pill + 白字——旧代码浅色底 #F7F7F7 配纯白标签，白底白字看不见；
        // 深色：深灰卡 + 白字；磨砂：白底半透明卡 + 深字 + 描边。
        // transparent：无底无边框，白字 + 阴影（浮在直播画面上才看得清）。
        Color bg, label, num, line;
        if (transparent)
        {
            bg = Parse("#000000");
            label = Color.FromArgb(220, 255, 255, 255);
            num = Parse("#FFFFFF");
            line = Color.FromArgb(0, 0, 0, 0);
        }
        else if (frosted)
        {
            bg = Parse("#FFFFFF");
            label = Color.FromArgb(184, 17, 17, 17);
            num = Parse("#111111");
            line = Color.FromArgb(110, 0, 0, 0);
        }
        else if (dark)
        {
            bg = Parse("#2A2A2A");
            label = Color.FromArgb(184, 255, 255, 255);
            num = Parse("#FFFFFF");
            line = Color.FromArgb(90, 255, 255, 255);
        }
        else
        {
            bg = Parse("#333333");
            label = Color.FromArgb(184, 255, 255, 255);
            num = Parse("#FFFFFF");
            line = Color.FromArgb(140, 0, 0, 0);
        }
        var font = FontHelper.FamilyForCards(state.CardFont, state.CjkFont, state.JpFont);
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        if (scale <= 0) scale = 1;
        var winW = Math.Max(80, AppWindow.Size.Width / scale);
        var winH = Math.Max(36, AppWindow.Size.Height / scale);
        var barH = compact ? 42 : winH;
        var fontSize = Math.Max(11, vertical
            ? Math.Min(barH / 4.2, winW / 6.4)
            : Math.Min(barH * 0.42, winW / 19.5));
        var panel = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = vertical ? 1 : 12,
            HorizontalAlignment = vertical ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        void Add(string caption, string value)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var captionBlock = new TextBlock
            {
                Text = caption,
                FontFamily = font,
                FontSize = Math.Max(11, fontSize * 0.92),
                Foreground = new SolidColorBrush(label),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(ShadowText(captionBlock, transparent ? 0.7 : 0));
            row.Children.Add(new TextBlock
            {
                Text = value,
                FontFamily = font,
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(num),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(row);
        }
        Add(vertical ? "点赞" : "本场点赞", FormatCount(LiveService.Likes));
        Add("弹幕", FormatCount(LiveService.Danmaku));
        Add(vertical ? "在线" : "当前在线", FormatCount(LiveService.Online));
        if (vertical && !string.IsNullOrWhiteSpace(LiveService.Watched)) Add("看过", LiveService.Watched);
        var radius = !vertical && state.Corner <= 0 ? barH / 2 : state.Corner <= 0 ? Math.Min(28, winW * 0.22) : state.Corner;
        return new Border
        {
            Background = CardBackground(state, bg, transparent),
            BorderBrush = new SolidColorBrush(line),
            BorderThickness = transparent ? new Thickness(0) : new Thickness(1),
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(vertical ? 16 : 18, vertical ? 10 : 0, vertical ? 16 : 18, vertical ? 10 : 0),
            MinHeight = compact || vertical ? 0 : barH,
            Height = compact ? 42 : vertical ? double.NaN : barH,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = compact ? VerticalAlignment.Top : VerticalAlignment.Stretch,
            Margin = compact ? new Thickness(6, 6, 6, 0) : new Thickness(0),
            Child = panel
        };
    }

    // 空窗只留一张左下角的提示卡，不假装成一条带「等」头像的弹幕，也不铺黑底。
    static Border EmptyState(AppState state, string title = "等待弹幕", string subtitle = "连接后会显示在这里")
    {
        var dark = IsCardDark(state);
        var transparent = string.Equals(state.CardThemeMode, "transparent", StringComparison.OrdinalIgnoreCase);
        var ink = !state.AdaptiveText ? Parse(state.TextColor) : dark ? Parse("#FFFFFF") : Parse("#1A1A1A");
        var font = FontHelper.FamilyForCards(state.CardFont, state.CjkFont, state.JpFont);
        var weight = WeightOf(state.DanmakuFontWeight);
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = font,
            FontSize = state.FontSize,
            FontWeight = weight,
            Foreground = new SolidColorBrush(ink)
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontFamily = font,
            FontSize = Math.Max(12, state.FontSize - 3),
            Foreground = new SolidColorBrush(Color.FromArgb(170, ink.R, ink.G, ink.B)),
            TextWrapping = TextWrapping.Wrap
        });
        // 空卡片也按三种样式画：和弹幕卡片同一套底色 + 1dp Line 描边；
        // 以前 Background 是 null、BorderThickness 是 0，描边刷子配了却没画出来。
        var baseColor = dark ? Parse("#2B2B2B") : Parse(state.CardColor);
        return new Border
        {
            Background = CardBackground(state, baseColor, transparent),
            BorderBrush = new SolidColorBrush(CardLineColor(state, dark, null, transparent, ink)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(state.Corner <= 0 ? 16 : state.Corner),
            Padding = new Thickness(14, 10, 14, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 360,
            Child = stack
        };
    }

    static UIElement LyricView(LyricLine line, bool current, AppState state, double position, bool allowWords)
    {
        // 没有逐字时间戳就整行显示。不按字数把一行均匀切开。
        if (!allowWords || !current || !state.LyricWordByWord || line.Words.Count == 0)
            return Lyric(line.Text, current, state);
        return LyricWords(line, state, position);
    }

    static UIElement LyricWords(LyricLine line, AppState state, double position)
    {
        var active = LyricsService.WordIndexAt(line, position);
        var align = state.LyricAlign == "left" ? TextAlignment.Left : state.LyricAlign == "right" ? TextAlignment.Right : TextAlignment.Center;
        var hAlign = state.LyricAlign == "left" ? HorizontalAlignment.Left : state.LyricAlign == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        var font = FontHelper.FamilyForCards(state.LyricFont, state.CjkFont, state.JpFont);
        var weight = LyricsDesigns.WeightOf(state.LyricWeight);
        var size = state.LyricSize;
        var sung = Parse(state.LyricHighlight);
        var idle = Parse(state.LyricColor);
        if (sung.R == idle.R && sung.G == idle.G && sung.B == idle.B)
            idle = Color.FromArgb(120, idle.R, idle.G, idle.B);
        FrameworkElement Make(bool shadow)
        {
            Color ColorAt(int index)
            {
                if (shadow) return Color.FromArgb(180, 0, 0, 0);
                return index <= active ? sung : idle;
            }
            if (state.LyricDirection == "vertical")
            {
                var column = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
                for (var i = 0; i < line.Words.Count; i++)
                {
                    var color = ColorAt(i);
                    foreach (var rune in line.Words[i].Text.EnumerateRunes())
                        column.Children.Add(new TextBlock
                        {
                            Text = rune.ToString(),
                            FontFamily = font,
                            FontSize = size,
                            FontWeight = weight,
                            Foreground = new SolidColorBrush(color),
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                }
                return column;
            }
            var rich = new RichTextBlock
            {
                FontFamily = font,
                FontSize = size,
                FontWeight = weight,
                TextAlignment = align,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = hAlign
            };
            var para = new Paragraph();
            for (var i = 0; i < line.Words.Count; i++)
                para.Inlines.Add(new Run { Text = line.Words[i].Text, Foreground = new SolidColorBrush(ColorAt(i)) });
            rich.Blocks.Add(para);
            return rich;
        }
        var stack = new Grid();
        if (state.LyricOutline)
        {
            var edge = Make(true);
            edge.Margin = new Thickness(1, 0, 0, 0);
            stack.Children.Add(edge);
        }
        else if (state.LyricShadow)
        {
            var ghost = Make(true);
            ghost.Margin = new Thickness(2, 2, 0, 0);
            stack.Children.Add(ghost);
        }
        stack.Children.Add(Make(false));
        var bg = Color.FromArgb((byte)(Math.Clamp(state.LyricBg, 0, 1) * 255), 0, 0, 0);
        return new Border { Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4, 12, 4), Child = stack, HorizontalAlignment = HorizontalAlignment.Center };
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
        return new Border { Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4, 12, 4), Child = stack, HorizontalAlignment = HorizontalAlignment.Center };
    }

    public static Color Parse(string hex)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var n))
            return Color.FromArgb(255, 26, 26, 26);
        return Color.FromArgb(255, (byte)(n >> 16), (byte)(n >> 8), (byte)n);
    }
}
