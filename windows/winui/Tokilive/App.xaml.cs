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
        InitializeComponent();
        UnhandledException += (_, e) => e.Handled = true;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _queue = DispatcherQueue.GetForCurrentThread();
        Shell = new MainWindow();
        Shell.Activate();
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
