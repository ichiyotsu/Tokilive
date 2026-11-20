using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Tokilive;

// 启动画面窗口：585x360，居中在主显示器，显示嵌入资源里的启动图
// （白框圆角卡片 + 极光渐变 + 发光字标，四角透明）。无边框、透明底，
// 进场 .55s（淡入 + 0.86 倍缩放），退出 .3s（淡出 + 0.94 倍缩放）。
public sealed partial class SplashWindow : Window
{
    const int Width = 585;
    const int Height = 360;

    public SplashWindow()
    {
        InitializeComponent();
        // 无边框、常驻最前、不可改大小，和悬浮窗同一套做法；
        // noActivate: false，启动画面正常显示。
        NativeWin.ApplyOverlay(this, "TokiLive", clickThrough: false, resizable: false, noActivate: false);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
            area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2, Width, Height));
        // 2 秒内点住也能拖走，和 1.2.0 的 movable 一样。
        Root.PointerPressed += (_, _) => NativeWin.DragWindow(NativeWin.Hwnd(this));
        Root.Loaded += async (_, _) =>
        {
            await LoadSplashImageAsync();
            PlayEntrance();
        };
    }

    // 启动图以嵌入资源打包（splash.png），启动时解码成位图。
    // 载入失败时窗口保持空白，不影响继续启动。
    async Task LoadSplashImageAsync()
    {
        try
        {
            await using var src = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Tokilive.splash.png");
            if (src == null) return;
            var mem = new MemoryStream();
            await src.CopyToAsync(mem);
            mem.Position = 0;
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(mem.AsRandomAccessStream());
            SplashImage.Source = bmp;
        }
        catch { /* 图片载入失败时启动画面保持空白，不影响启动 */ }
    }

    void PlayEntrance()
    {
        // 1.2.0 的 in .55s cubic-bezier(.2,0,0,1)：淡入 + 0.86 倍放大。
        Card.Opacity = 0;
        Card.RenderTransform = new ScaleTransform { ScaleX = 0.86, ScaleY = 0.86 };
        Card.RenderTransformOrigin = new Point(0.5, 0.5);
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
