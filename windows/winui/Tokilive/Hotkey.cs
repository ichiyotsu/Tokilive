using System.Runtime.InteropServices;

namespace Tokilive;

public static class Hotkey
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;
    const int WM_HOTKEY = 0x0312;
    const int VK_OEM_2 = 0xBF;
    const uint MOD_ALT = 0x0001;
    const uint MOD_CONTROL = 0x0002;
    const uint MOD_SHIFT = 0x0004;
    const uint MOD_WIN = 0x0008;
    // RegisterHotKey 的两个 id：1 是输入热键（AppState.Hotkey），
    // 2 是锁定热键（AppState.LockHotkey）。NativeWin 的子类收到
    // WM_HOTKEY 后按 wParam 调 DispatchHotKey 分发。
    const int InputHotkeyId = 1;
    const int LockHotkeyId = 2;
    public static Action? OnPressed;
    // 锁定热键（默认 Control+Shift+L）被按下时触发，由 MainWindow 接线。
    public static event Action? LockHotkeyPressed;
    static HookProc? _proc;
    static IntPtr _hook;
    static IntPtr _hwnd;
    static int _id;
    static int _lockId;

    public static void Bind(IntPtr hwnd, Action toggle)
    {
        Unbind();
        OnPressed = toggle;
        _hwnd = hwnd;
        var spec = AppState.Current.Hotkey.Trim();
        if (spec is "/" or "／" or "")
        {
            // 1.2.0 hotkey-policy：单独的 '/' 不能全局注册成热键，否则会吃掉
            // 游戏和输入框里的斜杠。只在 tokilive 自己的窗口处于前台时拦截，
            // 其他应用在前台时原样放行。
            _proc = (nCode, wParam, lParam) =>
            {
                if (nCode >= 0 && wParam == WM_KEYDOWN)
                {
                    var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    if (info.vkCode == VK_OEM_2 && (info.flags & 0x20) == 0 && TokiliveForeground() && CaretFree())
                    {
                        App.UI(toggle);
                        return (IntPtr)1;
                    }
                }
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            };
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        }
        else if (TryParseHotkey(spec, out var mods, out var vk))
        {
            _id = InputHotkeyId;
            if (!RegisterHotKey(hwnd, _id, mods, vk)) _id = 0;
        }
        // 锁定热键和输入热键分开注册：改输入热键不会断掉锁定热键。
        Reapply();
    }

    public static void Unbind()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        if (_id != 0 && _hwnd != IntPtr.Zero) { UnregisterHotKey(_hwnd, _id); _id = 0; }
        if (_lockId != 0 && _hwnd != IntPtr.Zero) { UnregisterHotKey(_hwnd, _lockId); _lockId = 0; }
    }

    /// <summary>重新注册锁定热键（设置 UI 改了 LockHotkey 后调用）。
    /// lockHotkey 为空时读 AppState.Current.LockHotkey（默认 "Control+Shift+L"）。
    /// 1.2.0 的 "CommandOrControl+Shift+L" 写法也接受，CommandOrControl 在
    /// Windows 上就是 Control。空串、无修饰键的组合、注册失败都返回 false，
    /// 且不注册（单独的按键不能全局占用，否则会吃掉正常输入）。</summary>
    public static bool Reapply(string? lockHotkey = null)
    {
        if (_lockId != 0 && _hwnd != IntPtr.Zero) { UnregisterHotKey(_hwnd, _lockId); _lockId = 0; }
        var spec = (lockHotkey ?? AppState.Current.LockHotkey ?? "").Trim();
        if (spec.Length == 0) return false;
        if (!TryParseHotkey(spec, out var mods, out var vk)) return false;
        if (mods == 0) return false;
        if (_hwnd == IntPtr.Zero) return false;
        _lockId = LockHotkeyId;
        if (!RegisterHotKey(_hwnd, _lockId, mods, vk)) { _lockId = 0; return false; }
        return true;
    }

    // NativeWin 的子类收到 WM_HOTKEY 时调用，按 id 分发给输入热键或锁定热键。
    public static void DispatchHotKey(int id)
    {
        if (id == LockHotkeyId && _lockId != 0) LockHotkeyPressed?.Invoke();
        else if (id == InputHotkeyId && _id != 0) OnPressed?.Invoke();
    }

    public static bool IsHotMessage(uint msg) => msg == WM_HOTKEY;

    static bool TryParseHotkey(string spec, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        var parts = spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var part in parts)
        {
            var token = part.Trim();
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Control", StringComparison.OrdinalIgnoreCase)
                || token.Equals("CommandOrControl", StringComparison.OrdinalIgnoreCase)) mods |= MOD_CONTROL;
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= MOD_ALT;
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= MOD_SHIFT;
            else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Meta", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Super", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Command", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Cmd", StringComparison.OrdinalIgnoreCase)) mods |= MOD_WIN;
            else if (token == "/") vk = VK_OEM_2;
            else if (token.Length == 1) vk = char.ToUpperInvariant(token[0]);
            else if (token.Length is 2 or 3 && token[0] is 'F' or 'f'
                && int.TryParse(token.AsSpan(1), out var f) && f is >= 1 and <= 24) vk = 0x70u + (uint)(f - 1);
            else return false;
        }
        return vk != 0;
    }

    // 前台窗口是不是 tokilive 自己的窗口：是才拦截 '/'，否则放行。
    static bool TokiliveForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    static bool CaretFree()
    {
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        var fg = GetForegroundWindow();
        var thread = GetWindowThreadProcessId(fg, out _);
        if (!GetGUIThreadInfo(thread, ref info)) return true;
        return info.hwndCaret == IntPtr.Zero;
    }

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    [StructLayout(LayoutKind.Sequential)]
    struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
}
