using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;

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
        // #31 单实例：第二个进程不建窗口、不出启动画面，直接把激活
        // 转给已经在跑的实例（主窗口弹到前台），然后自己退出。
        if (!TryClaimSingleInstance()) return;
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

    // #31：AppInstance.FindOrRegisterForKey 需要包标识；本工程是未打包
    //（WindowsPackageType None），在真机上它可能直接抛异常，所以包一层
    // try/catch：失败时退回命名互斥体——第二个进程直接退出（拉不起已在跑
    // 的窗口，只能保证不双开），第一个进程照常启动。两种路径下第二个进程
    // 都不会走到启动画面。是否走了 AppInstance 还是互斥体，必须在 Windows
    // 上实测（未打包 + WinAppSDK 1.6 的行为这里验证不了）。
    static Mutex? _singleMutex;
    static bool TryClaimSingleInstance()
    {
        try
        {
            var main = AppInstance.FindOrRegisterForKey("tokilive");
            if (!main.IsCurrent)
            {
                var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
                _ = main.RedirectActivationToAsync(activated);
                return false;
            }
            main.Activated += (_, _) => UI(() =>
            {
                if (Shell == null) return;
                Shell.AppWindow.Show();
                Shell.Activate();
            });
            return true;
        }
        catch
        {
            try
            {
                _singleMutex = new Mutex(true, "tokilive-single-instance", out var createdNew);
                return createdNew;
            }
            catch { return true; }
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
