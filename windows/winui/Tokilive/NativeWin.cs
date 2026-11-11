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
    const int WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_SHOWWINDOW = 0x0040;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    static readonly IntPtr HWND_NOTOPMOST = new(-2);
    const int SW_HIDE = 0;
    const int SW_SHOWNA = 8;
    const uint SRCCOPY = 0x00CC0020;
    const int NIF_MESSAGE = 0x1;
    const int NIF_ICON = 0x2;
    const int NIF_TIP = 0x4;
    const int NIM_ADD = 0x0;
    const int NIM_DELETE = 0x2;
    const int WM_LBUTTONUP = 0x0202;
    const int WM_LBUTTONDBLCLK = 0x0203;
    const int WM_APP = 0x8000;
    const uint WCA_ACCENT_POLICY = 19;
    const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;

    public static IntPtr Hwnd(Window window) => WindowNative.GetWindowHandle(window);
    public static AppWindow ForWindow(Window window) => AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(Hwnd(window)));

    public static void ApplyMain(Window window)
    {
        var hwnd = Hwnd(window);
        ForWindow(window).Title = "tokilive · 设置";
        RaiseAbove(hwnd, overlays: false);
        InstallSubclass(hwnd);
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
        ex |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        if (noActivate) ex |= WS_EX_NOACTIVATE; else ex &= ~WS_EX_NOACTIVATE;
        if (clickThrough) ex |= WS_EX_TRANSPARENT; else ex &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
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
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public static void PlaceBelow(IntPtr overlay, IntPtr main)
    {
        if (overlay == IntPtr.Zero || main == IntPtr.Zero) return;
        SetWindowPos(main, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(overlay, main, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static void RaiseAbove(IntPtr hwnd, bool overlays)
    {
        SetWindowPos(hwnd, overlays ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | (overlays ? SWP_NOACTIVATE : 0));
    }

    /// <summary>短暂藏起自己的窗口，再从屏幕 DC 取窗口矩形里的像素。默认关闭，约 2 秒一次。</summary>
    public static bool? SampleBehindIsLight(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return null;
        int w = rect.right - rect.left;
        int h = rect.bottom - rect.top;
        if (w < 8 || h < 8) return null;
        ShowWindow(hwnd, SW_HIDE);
        long sum = 0;
        var count = 0;
        var screen = GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, 16, 16);
        var old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, 16, 16, screen, rect.left + w / 2 - 8, rect.top + h / 2 - 8, SRCCOPY)) return null;
            for (var y = 0; y < 16; y += 4)
            for (var x = 0; x < 16; x += 4)
            {
                var px = (uint)GetPixel(mem, x, y);
                if (px == 0xFFFFFFFF) continue;
                int r = (int)(px & 0xFF), g = (int)((px >> 8) & 0xFF), b = (int)((px >> 16) & 0xFF);
                sum += (long)(0.2126 * r + 0.7152 * g + 0.0722 * b);
                count++;
            }
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
            ShowWindow(hwnd, SW_SHOWNA);
        }
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
        }
        if (msg == 0x0312) global::Tokilive.App.UI(() => Hotkey.OnPressed?.Invoke());
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
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hdc, int x, int y);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadIcon(IntPtr instance, IntPtr icon);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
