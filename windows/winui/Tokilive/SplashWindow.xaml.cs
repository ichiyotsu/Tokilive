using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Tokilive;

// 启动动画窗口：1.2.0 splash.html 的 WinUI 版，384x256，居中在主显示器。
// 底色是左上 #06344b 到右下 #420937 的渐变。进场 .55s（淡入 + 0.86 倍
// 缩放），标题再带一点上浮；退出 .3s（淡出 + 0.94 倍缩放）。
public sealed partial class SplashWindow : Window
{
    const int Width = 384;
    const int Height = 256;

    public SplashWindow()
    {
        InitializeComponent();
        // 无边框、常驻最前、不可改大小，和悬浮窗同一套做法；
        // noActivate: false，启动画面正常显示。
        NativeWin.ApplyOverlay(this, "tokilive", clickThrough: false, resizable: false, noActivate: false);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
            area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2, Width, Height));
        // 2 秒内点住也能拖走，和 1.2.0 的 movable 一样。
        Root.PointerPressed += (_, _) => NativeWin.DragWindow(NativeWin.Hwnd(this));
        Loaded += (_, _) => PlayEntrance();
    }

    void PlayEntrance()
    {
        // 1.2.0 的 in .55s cubic-bezier(.2,0,0,1)：淡入 + 0.86 倍放大；
        // 标题再 .8s、延迟 .1s 上浮 10px 淡入。
        Card.Opacity = 0;
        Card.RenderTransform = new ScaleTransform { ScaleX = 0.86, ScaleY = 0.86 };
        Card.RenderTransformOrigin = new Point(0.5, 0.5);
        Title.Opacity = 0;
        Title.RenderTransform = new CompositeTransform { TranslateY = 10 };
        var sb = new Storyboard();
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(550)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fade, Card);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);
        foreach (var prop in new[] { "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", "(UIElement.RenderTransform).(ScaleTransform.ScaleY)" })
        {
            var a = new DoubleAnimation { From = 0.86, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(550)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, Card);
            Storyboard.SetTargetProperty(a, prop);
            sb.Children.Add(a);
        }
        var titleFade = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(800)), BeginTime = TimeSpan.FromMilliseconds(100), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(titleFade, Title);
        Storyboard.SetTargetProperty(titleFade, "Opacity");
        sb.Children.Add(titleFade);
        var titleRise = new DoubleAnimation { From = 10, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(800)), BeginTime = TimeSpan.FromMilliseconds(100), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(titleRise, Title);
        Storyboard.SetTargetProperty(titleRise, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)");
        sb.Children.Add(titleRise);
        sb.Begin();
    }

    // 退出：1.2.0 的 out .3s，淡出 + 缩到 0.94 倍。播完才算完成。
    public Task PlayExitAsync()
    {
        var done = new TaskCompletionSource();
        var sb = new Storyboard();
        var fade = new DoubleAnimation { From = 1, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(300)) };
        Storyboard.SetTarget(fade, Card);
        Storyboard.SetTargetProperty(fade, "Opacity");
        sb.Children.Add(fade);
        foreach (var prop in new[] { "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", "(UIElement.RenderTransform).(ScaleTransform.ScaleY)" })
        {
            var a = new DoubleAnimation { From = 1, To = 0.94, Duration = new Duration(TimeSpan.FromMilliseconds(300)) };
            Storyboard.SetTarget(a, Card);
            Storyboard.SetTargetProperty(a, prop);
            sb.Children.Add(a);
        }
        sb.Completed += (_, _) => done.TrySetResult();
        sb.Begin();
        return done.Task;
    }
}
