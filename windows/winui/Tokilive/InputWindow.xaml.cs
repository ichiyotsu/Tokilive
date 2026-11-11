using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Tokilive;

public sealed partial class InputWindow : Window
{
    const int DefaultX = 200, DefaultY = 640, DefaultW = 460, DefaultH = 64;

    Windows.Graphics.PointInt32? _drag;
    Windows.Foundation.Point _start;
    // 落盘防抖：拖动/改大小时 AppWindow.Changed 会连续触发，只写 AppState
    // 字段（Set 会去重），停 1 秒没再动才写一次 config.json；关闭时补一次。
    // 不是轮询：没有变化时这个计时器永远不会跑。
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public InputWindow()
    {
        InitializeComponent();
        NativeWin.ApplyOverlay(this, "tokilive · 输入", clickThrough: false, resizable: !AppState.Current.InputLocked, noActivate: false);
        RestoreBounds();
        // 移动和改大小都走 AppWindow.Changed（DidPositionChange /
        // DidSizeChange），不用轮询，也不用再等主窗口的 KeepOrder。
        // 恢复时那次 MoveAndResize 发生在订阅之前，不会反过来触发保存。
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveQuiet(); };
        AppWindow.Changed += (_, args) =>
        {
            if (!args.DidPositionChange && !args.DidSizeChange) return;
            Remember();
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        Closed += (_, _) => { Remember(); SaveQuiet(); };
        Root.PointerPressed += Pressed;
        Root.PointerMoved += Moved;
        Root.PointerReleased += (_, _) => _drag = null;
    }

    // #39：有保存过的位置就恢复它。AppState 默认全是 0，“从没保存过”
    // 的标记其实是宽/高为 0——X/Y 不能拿“非零”判断，0 和负数都是合法
    // 坐标（主屏左边/上边的显示器就是负坐标）。宽/高 <=0 或矩形和当前
    // 虚拟屏幕（全部显示器合起来的范围）完全不相交时，用默认位置，
    // 免得换显示器/改分辨率后窗口落在屏外找不回来。
    public void RestoreBounds()
    {
        var state = AppState.Current;
        var (x, y, w, h) = (state.InputX, state.InputY, state.InputW, state.InputH);
        if (w <= 0 || h <= 0 || !IntersectsVirtualScreen(x, y, w, h))
        {
            x = DefaultX;
            y = DefaultY;
            w = DefaultW;
            h = DefaultH;
        }
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(x, y, w, h));
    }

    // 和 OverlayWindow.Remember() 一个写法：读当前窗口位置/大小写回
    // AppState 字段（Set 会触发 Changed）；窗口藏起来时位置不可信，
    // 沿用它那个 IsVisible 守卫——拖动时窗口本来就是可见的。
    public void Remember()
    {
        if (!AppWindow.IsVisible) return;
        var p = AppWindow.Position;
        var s = AppWindow.Size;
        var state = AppState.Current;
        state.InputX = p.X;
        state.InputY = p.Y;
        state.InputW = s.Width;
        state.InputH = s.Height;
    }

    static void SaveQuiet()
    {
        try { AppState.Current.Save(); }
        catch { /* 配置写不出去不影响输入窗本身 */ }
    }

    static bool IntersectsVirtualScreen(int x, int y, int w, int h)
    {
        try
        {
            var vx = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
            var vy = GetSystemMetrics(77); // SM_YVIRTUALSCREEN
            var vw = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
            var vh = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
            if (vw <= 0 || vh <= 0) return true; // 拿不到就别拦着恢复
            return x < vx + vw && x + w > vx && y < vy + vh && y + h > vy;
        }
        catch { return true; }
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

    [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);
}
