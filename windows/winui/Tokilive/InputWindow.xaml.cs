using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Tokilive;

public sealed partial class InputWindow : Window
{
    Windows.Graphics.PointInt32? _drag;
    Windows.Foundation.Point _start;

    public InputWindow()
    {
        InitializeComponent();
        NativeWin.ApplyOverlay(this, "tokilive · 输入", clickThrough: false, resizable: !AppState.Current.InputLocked, noActivate: false);
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(200, 640, 460, 64));
        Root.PointerPressed += Pressed;
        Root.PointerMoved += Moved;
        Root.PointerReleased += (_, _) => _drag = null;
    }

    public void Toggle()
    {
        if (AppWindow.IsVisible) { AppWindow.Hide(); return; }
        AppWindow.Show();
        Activate();
        Box.Focus(FocusState.Programmatic);
    }

    public IntPtr Handle => NativeWin.Hwnd(this);

    void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (AppState.Current.InputLocked) return;
        _drag = AppWindow.Position;
        _start = e.GetCurrentPoint(Root).Position;
        Root.CapturePointer(e.Pointer);
    }

    void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == null || AppState.Current.InputLocked) return;
        var p = e.GetCurrentPoint(Root).Position;
        AppWindow.Move(new global::Windows.Graphics.PointInt32(_drag.Value.X + (int)(p.X - _start.X), _drag.Value.Y + (int)(p.Y - _start.Y)));
    }

    async void Box_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        var snapshot = Box.Text;
        await Task.Delay(30);
        if (Box.Text != snapshot) return;
        var text = Box.Text;
        Box.Text = "";
        var message = await LiveService.Send(text);
        AppState.Current.Status = message;
        if (message == "已发送") LiveService.PushLocal("danmaku", "我", text);
    }

    void Lock_Click(object sender, RoutedEventArgs e)
    {
        AppState.Current.InputLocked = !AppState.Current.InputLocked;
        NativeWin.ApplyOverlay(this, "tokilive · 输入", false, !AppState.Current.InputLocked, false);
    }
}
