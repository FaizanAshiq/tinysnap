using System.Runtime.InteropServices;

namespace Tinysnap.Windows;

/// <summary>The Win32 calls the platform layer needs, and nothing more.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    internal delegate bool MonitorEnumProc(nint monitor, nint hdc, ref RECT rect, nint data);
    internal delegate bool WindowEnumProc(nint window, nint data);
    internal delegate nint WindowProc(nint window, uint message, nint wParam, nint lParam);

    internal const uint SRCCOPY = 0x00CC0020;
    internal const int GWL_EXSTYLE = -20;
    internal const long WS_EX_TOOLWINDOW = 0x00000080;
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const int DWMWA_CLOAKED = 14;
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    internal static readonly nint HWND_MESSAGE = -3;
    internal static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    internal const uint SPI_GETCLIENTAREAANIMATION = 0x1042, SPI_GETDESKWALLPAPER = 0x0073;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfoW(uint action, uint param, [Out] char[] value, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfoW(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    internal static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint window, nint hdc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint hdc, nint gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint hdc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(WindowEnumProc callback, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    internal static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(nint window, char[] text, int length);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(nint window, char[] name, int length);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out RECT rect);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint window, int attribute, out RECT value, int size);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    internal const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y,
                                                int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);

    // The keyboard hook that takes Print Screen, on a thread of its own.

    internal delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    internal const int WH_KEYBOARD_LL = 13;
    internal const uint WM_QUIT = 0x0012, WM_TIMER = 0x0113, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookExW(int hook, HookProc procedure, nint module, uint thread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern int GetMessageW(out MSG message, nint window, uint first, uint last);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessageW(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    internal static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    internal const uint CF_UNICODETEXT = 13;
    internal const uint CF_DIBV5 = 17;
    internal const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterClipboardFormatW(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    internal static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();

    [DllImport("kernel32.dll")]
    internal static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    internal static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll")]
    internal static extern nint GlobalFree(nint memory);

    /// <summary>Physical pixels for the calls in the block, whatever the process declared, so the
    /// monitors, the pointer and the windows all come back in one coordinate space.</summary>
    internal readonly struct PerMonitorPixels : IDisposable
    {
        private readonly nint previous;

        public PerMonitorPixels() => previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        public void Dispose()
        {
            if (previous != 0) SetThreadDpiAwarenessContext(previous);
        }
    }
}
