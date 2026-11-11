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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _queue = DispatcherQueue.GetForCurrentThread();
            Shell = new MainWindow();
            Shell.Activate();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
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
