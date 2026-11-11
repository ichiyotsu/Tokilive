using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tokilive;

public partial class App : Application
{
    public static MainWindow? Shell { get; private set; }
    static DispatcherQueue? _queue;

    public App()
    {
        UnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Report(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report(e.Exception);
            e.SetObserved();
        };
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _queue = DispatcherQueue.GetForCurrentThread();
        SplashWindow? splash = null;
        try
        {
            // 1.2.0 启动时先出启动画面（384x256，约 2 秒），主窗口建好后
            // 再淡出。启动画面出问题就直接进主窗口，和 1.2.0 在 createSplash
            // 外包 try/catch 的做法一样，不因此崩掉。
            try
            {
                splash = new SplashWindow();
                splash.Activate();
            }
            catch { splash = null; }
            var shownAt = DateTimeOffset.Now;
            Shell = new MainWindow();
            if (splash != null)
            {
                var wait = TimeSpan.FromSeconds(2) - (DateTimeOffset.Now - shownAt);
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
                try { await splash.PlayExitAsync(); } catch { }
                try { splash.Close(); } catch { }
            }
            Shell.Activate();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    static void OnUnhandled(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (e.Exception is Exception ex) Report(ex);
        e.Handled = true;
    }

    static void Report(Exception ex)
    {
        CrashLog.Write(ex);
        CrashLog.Show(ex);
        Environment.Exit(1);
    }

    public static void UI(Action action)
    {
        if (_queue == null || _queue.HasThreadAccess) action();
        else _queue.TryEnqueue(() => action());
    }

    public static Task<bool> Confirm(string text)
    {
        var source = new TaskCompletionSource<bool>();
        UI(async () =>
        {
            if (Shell == null) { source.SetResult(false); return; }
            var dialog = new ContentDialog
            {
                Title = "tokilive",
                Content = text,
                PrimaryButtonText = "继续",
                CloseButtonText = "取消",
                XamlRoot = Shell.Content.XamlRoot
            };
            source.SetResult(await dialog.ShowAsync() == ContentDialogResult.Primary);
        });
        return source.Task;
    }
}
