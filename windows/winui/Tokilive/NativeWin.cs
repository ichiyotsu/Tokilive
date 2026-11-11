using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Tokilive;

public static class NativeWin
{
    const int GWL_STYLE = -16;
    const int GWL_EXSTYLE = -20;
    const int WS_CAPTION = 0x00C00000;
    const int WS_THICKFRAME = 0x00040000;
    const int WS_SYSMENU = 0x00080000;
    const int WS_MINIMIZEBOX = 0x00020000;
    const int WS_MAXIMIZEBOX = 0x00010000;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_APPWINDOW = 0x00040000;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_SHOWWINDOW = 0x0040;
    const uint SWP_FRAMECHANGED = 0x0020;
    const uint WDA_NONE = 0;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    static readonly IntPtr HWND_NOTOPMOST = new(-2);
    const int WM_NULL = 0x0000;
    const int NIF_MESSAGE = 0x1;
    const int NIF_ICON = 0x2;
    const int NIF_TIP = 0x4;
    const int NIM_ADD = 0x0;
    const int NIM_DELETE = 0x2;
    const int WM_LBUTTONUP = 0x0202;
    const int WM_LBUTTONDBLCLK = 0x0203;
    const int WM_RBUTTONUP = 0x0205;
    const int WM_HOTKEY = 0x0312;
    const int WM_APP = 0x8000;
    const uint WCA_ACCENT_POLICY = 19;
    const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;
    // 托盘右键菜单
    const uint MF_STRING = 0x0000;
    const uint MF_GRAYED = 0x0001;
    const uint MF_DISABLED = 0x0002;
    const uint MF_CHECKED = 0x0008;
    const uint MF_SEPARATOR = 0x0800;
    const uint TPM_LEFTALIGN = 0x0000;
    const uint TPM_RIGHTBUTTON = 0x0002;
    const uint TPM_RETURNCMD = 0x0100;
    const uint TrayMenuStatus = 1000;
    const uint TrayMenuSettings = 1001;
    const uint TrayMenuLockAll = 1002;
    const uint TrayMenuLockDanmaku = 1003;
    const uint TrayMenuLockLive = 1004;
    const uint TrayMenuDanmakuVisible = 1005;
    const uint TrayMenuLiveVisible = 1006;
    const uint TrayMenuTopmost = 1007;
    const uint TrayMenuReconnect = 1008;
    const uint TrayMenuExit = 1009;

    public static IntPtr Hwnd(Window window) => WindowNative.GetWindowHandle(window);
    public static AppWindow ForWindow(Window window) => AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(Hwnd(window)));

    public static void ApplyMain(Window window)
    {
        var hwnd = Hwnd(window);
        var app = ForWindow(window);
        app.Title = "tokilive · 设置";
        // 顶栏透明：把内容延伸进标题栏，标题栏那条就不再是系统单独画的
        // 有色条，而是窗口自己的背景（Mica / Acrylic）透出来。
        // 标题栏按钮的底色也全部改透明，悬停/按下只留一层很淡的灰，
        // 让按钮在浅色深色下都看得见，又不会再出现一块颜色。
        try
        {
            var bar = app.TitleBar;
            bar.ExtendsContentIntoTitleBar = true;
            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(20, 127, 127, 127);
            bar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(32, 127, 127, 127);
            // 前景色（按钮图标和文字）不改，留给系统按浅色/深色自己选。
        }
        catch { /* 老系统上标题栏 API 不可用就保持默认顶栏 */ }
        RaiseAbove(hwnd, overlays: false);
        InstallSubclass(hwnd);
    }

    // 拖动区域：标题栏整条（除右边的最小化/最大化/关闭三个按钮）。
    // SetDragRectangles 要的是物理像素：AppWindow.Size 和 TitleBar.Height
    // 本来就是物理像素，标题栏按钮宽按 46dip × 缩放换算。
    // 必须在窗口大小变化时重算（最大化/还原会触发 SizeChanged）。
    public static void UpdateMainDragRectangles(Window window)
    {
        try
        {
            var app = ForWindow(window);
            var scale = window.Content?.XamlRoot?.RasterizationScale ?? 1.0;
            var height = app.TitleBar.Height;
            var width = app.Size.Width;
            var captionWidth = (int)Math.Ceiling(46 * 3 * scale);
            var dragWidth = Math.Max(0, width - captionWidth);
            if (height <= 0 || dragWidth <= 0) return;
            app.TitleBar.SetDragRectangles(new[] { new global::Windows.Graphics.RectInt32(0, 0, dragWidth, height) });
        }
        catch { /* 拖动区设不上时，标题栏那行的 PointerPressed 会兜底拖动 */ }
    }

    public static void ApplyOverlay(Window window, string title, bool clickThrough, bool resizable, bool noActivate)
    {
        var hwnd = Hwnd(window);
        var app = ForWindow(window);
        app.Title = title;
        if (app.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = resizable && !clickThrough;
            presenter.IsAlwaysOnTop = true;
        }
        var style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        style &= ~(WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        if (resizable && !clickThrough) style |= WS_THICKFRAME;
        else style &= ~WS_THICKFRAME;
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));

        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        // 1.2.0 capture-win.js / capture-style.js：OBS / 直播姬的窗口枚举会
        // 跳过 WS_EX_TOOLWINDOW，所以去掉它、改加 WS_EX_APPWINDOW，让悬浮窗
        // 能被窗口捕获枚举到。分层透明（WS_EX_LAYERED）保留，否则不是真透明。
        // 代价：悬浮窗会出现在任务栏和 Alt-Tab 里，这是 1.2.0 就有的取舍。
        // 默认关掉这个兼容：保留 WS_EX_TOOLWINDOW，悬浮窗不进任务栏和
        // Alt-Tab，程序只留在托盘；只有 ObsCaptureCompat 开了才走 1.2.0
        // 的做法。显示亲和性两种情况都保持 WDA_NONE（它只管采集本身
        // 能不能进行，不管窗口在不在枚举列表里）。
        ex |= WS_EX_LAYERED;
        if (AppState.Current.ObsCaptureCompat)
        {
            ex |= WS_EX_APPWINDOW;
            ex &= ~WS_EX_TOOLWINDOW;
        }
        else
        {
            ex |= WS_EX_TOOLWINDOW;
            ex &= ~WS_EX_APPWINDOW;
        }
        if (noActivate) ex |= WS_EX_NOACTIVATE; else ex &= ~WS_EX_NOACTIVATE;
        if (clickThrough) ex |= WS_EX_TRANSPARENT; else ex &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        // WDA_EXCLUDEFROMCAPTURE (0x11) 会让系统直接拒绝采集，必须保持 WDA_NONE。
        SetWindowDisplayAffinity(hwnd, WDA_NONE);
        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var accent = new ACCENT_POLICY { AccentState = ACCENT_ENABLE_TRANSPARENTGRADIENT };
        var size = Marshal.SizeOf<ACCENT_POLICY>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
        // SWP_FRAMECHANGED：扩展样式改了要让系统重算窗口帧，否则
        // WS_EX_TOOLWINDOW 去掉 / WS_EX_APPWINDOW 加上可能不生效。
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
    }

    public static void PlaceBelow(IntPtr overlay, IntPtr main)
    {
        if (overlay == IntPtr.Zero || main == IntPtr.Zero) return;
        // 1.3.1 的 bug：这里先把主窗口顶到 HWND_TOPMOST，设置窗口就被
        // 永久顶在所有窗口之上，切到游戏/浏览器后 400ms 内又被盖回来。
        // 悬浮窗自己已经是 AlwaysOnTop，只需要把它插到主窗口下面。
        SetWindowPos(overlay, main, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // 无标题栏窗口的拖动：松开鼠标捕获，模拟按住标题栏。
    public static void DragWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
    }

    public static void RaiseAbove(IntPtr hwnd, bool overlays)
    {
        SetWindowPos(hwnd, overlays ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | (overlays ? SWP_NOACTIVATE : 0));
    }

    /// <summary>不隐藏窗口：直接从屏幕 DC 读窗口矩形外侧一圈的像素（上/下/左/右
    /// 各取几点，像 1.2.0 adaptive.js 的 luminanceOutside），按亮度均值判断
    /// 背景是亮是暗。窗口不再闪一下。默认约 2 秒调一次。</summary>
    public static bool? SampleBehindIsLight(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return null;
        int w = rect.right - rect.left;
        int h = rect.bottom - rect.top;
        if (w < 8 || h < 8) return null;
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        long sum = 0;
        var count = 0;
        try
        {
            // 窗口贴着屏幕边缘时，外侧点可能落在屏幕外，
            // GetPixel 会返回 CLR_INVALID (0xFFFFFFFF)，直接跳过。
            void Sample(int x, int y)
            {
                var px = GetPixel(screen, x, y);
                if (px == 0xFFFFFFFF) return;
                int r = (int)(px & 0xFF), g = (int)((px >> 8) & 0xFF), b = (int)((px >> 16) & 0xFF);
                sum += (long)(0.2126 * r + 0.7152 * g + 0.0722 * b);
                count++;
            }
            // 上下两条边外 8px，各取 5 个点；左右两条边外 8px，各取 3 个点。
            for (var i = 1; i <= 5; i++)
            {
                var x = rect.left + w * i / 6;
                Sample(x, rect.top - 8);
                Sample(x, rect.bottom + 8);
            }
            for (var i = 1; i <= 3; i++)
            {
                var y = rect.top + h * i / 4;
                Sample(rect.left - 8, y);
                Sample(rect.right + 8, y);
            }
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
        if (count == 0) return null;
        return sum / count > 160;
    }

    public static bool TryTray(IntPtr hwnd, Action onClick)
    {
        _click = onClick;
        _trayHwnd = hwnd;
        var data = Notify(hwnd);
        return Shell_NotifyIcon(NIM_ADD, ref data);
    }

    public static void RemoveTray(IntPtr hwnd)
    {
        var data = Notify(hwnd);
        Shell_NotifyIcon(NIM_DELETE, ref data);
    }

    // 托盘右键菜单：NativeWin 保持 UI 无关，菜单动作只通过事件发出去，
    // 由 MainWindow 接线（设置窗口、锁定、显隐、置顶、重连、退出）。
    // 状态文本/开关状态由下面这组 Func 提供，不提供时用默认值。
    public static event Action? TrayShowSettings;
    public static event Action? TrayToggleLockAll;
    public static event Action? TrayToggleDanmakuLock;
    public static event Action? TrayToggleLiveLock;
    public static event Action? TrayToggleDanmakuVisible;
    public static event Action? TrayToggleLiveVisible;
    public static event Action? TrayToggleTopmost;
    public static event Action? TrayReconnect;
    public static event Action? TrayExit;

    // 菜单打开时读一次，用来拼状态行和“锁定/解锁、显示/隐藏”的文案。
    // 不设置时：状态行用 AppState.Current.Status，其余默认未锁定、已显示、不置顶。
    public static Func<string>? TrayStatusText;
    public static Func<bool>? TrayAllLocked;
    public static Func<bool>? TrayDanmakuLocked;
    public static Func<bool>? TrayLiveLocked;
    public static Func<bool>? TrayDanmakuVisible;
    public static Func<bool>? TrayLiveVisible;
    public static Func<bool>? TrayTopmost;

    static void ShowTrayMenu(IntPtr hwnd)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            var status = TrayStatusText?.Invoke() ?? AppState.Current.Status;
            AppendMenu(menu, MF_STRING | MF_GRAYED | MF_DISABLED, TrayMenuStatus, status);
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, TrayMenuSettings, "设置");
            AppendMenu(menu, MF_STRING, TrayMenuLockAll, (TrayAllLocked?.Invoke() ?? false) ? "解锁全部窗口" : "锁定全部窗口");
            AppendMenu(menu, MF_STRING, TrayMenuLockDanmaku, (TrayDanmakuLocked?.Invoke() ?? false) ? "解锁弹幕窗" : "锁定弹幕窗");
            AppendMenu(menu, MF_STRING, TrayMenuLockLive, (TrayLiveLocked?.Invoke() ?? false) ? "解锁实时窗" : "锁定实时窗");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, TrayMenuDanmakuVisible, (TrayDanmakuVisible?.Invoke() ?? true) ? "隐藏弹幕窗" : "显示弹幕窗");
            AppendMenu(menu, MF_STRING, TrayMenuLiveVisible, (TrayLiveVisible?.Invoke() ?? true) ? "隐藏实时窗" : "显示实时窗");
            AppendMenu(menu, MF_STRING | ((TrayTopmost?.Invoke() ?? false) ? MF_CHECKED : 0), TrayMenuTopmost, "置顶");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, TrayMenuReconnect, "重新连接");
            AppendMenu(menu, MF_STRING, TrayMenuExit, "退出");
            GetCursorPos(out var pt);
            // TrackPopupMenu 要求调用前把窗口提到前台，结束后发一个 WM_NULL，
            // 否则菜单可能点不掉、一直赖在屏幕上。
            SetForegroundWindow(hwnd);
            var cmd = (uint)TrackPopupMenu(menu, TPM_LEFTALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.x, pt.y, 0, hwnd, IntPtr.Zero);
            PostMessage(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            switch (cmd)
            {
                case TrayMenuSettings: TrayShowSettings?.Invoke(); break;
                case TrayMenuLockAll: TrayToggleLockAll?.Invoke(); break;
                case TrayMenuLockDanmaku: TrayToggleDanmakuLock?.Invoke(); break;
                case TrayMenuLockLive: TrayToggleLiveLock?.Invoke(); break;
                case TrayMenuDanmakuVisible: TrayToggleDanmakuVisible?.Invoke(); break;
                case TrayMenuLiveVisible: TrayToggleLiveVisible?.Invoke(); break;
                case TrayMenuTopmost: TrayToggleTopmost?.Invoke(); break;
                case TrayMenuReconnect: TrayReconnect?.Invoke(); break;
                case TrayMenuExit: TrayExit?.Invoke(); break;
            }
        }
        finally { DestroyMenu(menu); }
    }

    static NOTIFYICONDATA Notify(IntPtr hwnd) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = hwnd,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_APP + 1,
        hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)),
        szTip = "tokilive"
    };

    static SUBCLASSPROC? _proc;
    static Action? _click;
    static IntPtr _trayHwnd;

    static void InstallSubclass(IntPtr hwnd)
    {
        _proc ??= Subclass;
        SetWindowSubclass(hwnd, _proc, 1, 0);
    }

    static IntPtr Subclass(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (msg == WM_APP + 1)
        {
            var code = lParam.ToInt32() & 0xFFFF;
            if (code is WM_LBUTTONUP or WM_LBUTTONDBLCLK) _click?.Invoke();
            else if (code == WM_RBUTTONUP) ShowTrayMenu(hWnd);
        }
        // RegisterHotKey 到达的消息：wParam 是热键 id，Hotkey 按 id 分发
        // 给输入热键（OnPressed）或锁定热键（LockHotkeyPressed）。
        if (msg == WM_HOTKEY) global::Tokilive.App.UI(() => Hotkey.DispatchHotKey((int)wParam));
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public static string? SystemAccent()
    {
        try
        {
            var c = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }
    [StructLayout(LayoutKind.Sequential)] struct ACCENT_POLICY { public int AccentState, AccentFlags, GradientColor, AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct WINDOWCOMPOSITIONATTRIBDATA { public uint Attribute; public IntPtr Data; public int SizeOfData; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
    }

    delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, nuint id, nuint data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hdc, int x, int y);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll")] static extern nuint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadIcon(IntPtr instance, IntPtr icon);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
