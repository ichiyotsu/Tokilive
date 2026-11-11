using System.Runtime.InteropServices;

namespace Tokilive;

public static class Hotkey
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;
    const int VK_OEM_2 = 0xBF;
    public static Action? OnPressed;
    static HookProc? _proc;
    static IntPtr _hook;
    static IntPtr _hwnd;
    static int _id;

    public static void Bind(IntPtr hwnd, Action toggle)
    {
        Unbind();
        OnPressed = toggle;
        _hwnd = hwnd;
        var spec = AppState.Current.Hotkey.Trim();
        if (spec is "/" or "／" or "")
        {
            _proc = (nCode, wParam, lParam) =>
            {
                if (nCode >= 0 && wParam == WM_KEYDOWN)
                {
                    var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    if (info.vkCode == VK_OEM_2 && (info.flags & 0x20) == 0 && CaretFree())
                    {
                        App.UI(toggle);
                        return (IntPtr)1;
                    }
                }
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            };
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            return;
        }
        uint mods = 0;
        uint vk = 0;
        foreach (var part in spec.Split('+'))
        {
            var token = part.Trim();
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= 0x0002;
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= 0x0001;
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= 0x0004;
            else if (token.Length == 1) vk = char.ToUpperInvariant(token[0]);
        }
        if (vk == 0) return;
        _id = 1;
        RegisterHotKey(hwnd, _id, mods, vk);
    }

    public static void Unbind()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        if (_id != 0 && _hwnd != IntPtr.Zero) { UnregisterHotKey(_hwnd, _id); _id = 0; }
    }

    public static bool IsHotMessage(uint msg) => msg == 0x0312;

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
