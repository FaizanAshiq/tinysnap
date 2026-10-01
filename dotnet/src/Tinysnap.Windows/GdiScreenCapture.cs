using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;
using Tinysnap.Core;
using Tinysnap.Platform;
using static Tinysnap.Windows.Native;

namespace Tinysnap.Windows;

/// <summary>Freezes every monitor with GDI's BitBlt, each at its own pixels and scale, and lists
/// the windows on them. DXGI desktop duplication is faster and reads HDR screens, and can replace
/// this if a real gap shows.</summary>
internal sealed class GdiScreenCapture : IScreenCapture
{
    public FrozenDesktop Freeze()
    {
        using var pixels = new PerMonitorPixels();
        var screens = new List<FrozenScreen>();
        foreach (var (bounds, scale) in Monitors())
            if (Grab(bounds) is { } image) screens.Add(new FrozenScreen(ToRect(bounds), scale, image));
        return new FrozenDesktop(screens, Win32Windows.List());
    }

    public Point PointerPosition()
    {
        using var pixels = new PerMonitorPixels();
        return GetCursorPos(out var point) ? new Point(point.X, point.Y) : Point.Zero;
    }

    /// <summary>Windows 11 rounds window corners by 8 points; Windows 10 leaves them square.</summary>
    public double WindowCornerRadius => Environment.OSVersion.Version.Build >= 22000 ? 8 : 0;

    /// <summary>None: windows are listed with the frozen screen and picked on the overlay.</summary>
    public Func<Task<Capture?>>? PickWindow => null;

    internal static Rect ToRect(RECT rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    /// <summary>Every monitor's bounds in physical pixels, and its scale from its effective DPI.</summary>
    internal static List<(RECT Bounds, double Scale)> Monitors()
    {
        var monitors = new List<(RECT, double)>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint _, ref RECT _, nint _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(monitor, ref info)) return true;
            var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 ? dpi / 96.0 : 1;
            monitors.Add((info.rcMonitor, scale));
            return true;
        }, 0);
        return monitors;
    }

    /// <summary>Sets every pixel's alpha byte in place, a vector's worth of pixels at a time:
    /// a 4K screen is eight million of them.</summary>
    private static unsafe void Opaque(nint bits, int count)
    {
        var pixels = new Span<uint>((void*)bits, count);
        var vectors = MemoryMarshal.Cast<uint, Vector<uint>>(pixels);
        var alpha = new Vector<uint>(0xFF000000);
        for (var i = 0; i < vectors.Length; i++) vectors[i] |= alpha;
        for (var i = vectors.Length * Vector<uint>.Count; i < count; i++) pixels[i] |= 0xFF000000;
    }

    /// <summary>One monitor's pixels. GDI leaves the alpha byte undefined, often zero, so it is set
    /// to opaque, or the capture would come out see-through.</summary>
    internal static SKImage? Grab(RECT bounds)
    {
        int width = bounds.Width, height = bounds.Height;
        if (width <= 0 || height <= 0) return null;
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        // Top-down, 32 bits a pixel, blue first.
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        var bitmap = CreateDIBSection(screen, ref header, 0, out var bits, 0, 0);
        var previous = SelectObject(memory, bitmap);
        try
        {
            // No CAPTUREBLT: the desktop is composed by the window manager, layered windows
            // included, and the flag only made the copy slower and the pointer flicker.
            if (bitmap == 0 || !BitBlt(memory, 0, 0, width, height, screen, bounds.Left, bounds.Top, SRCCOPY))
                return null;
            Opaque(bits, width * height);
            return SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque), bits, width * 4);
        }
        finally
        {
            SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }
}

/// <summary>The windows window mode can pick, front to back.</summary>
internal static class Win32Windows
{
    private static readonly HashSet<string> Desktop = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>Ordinary windows only: shown, not minimised, not cloaked by another virtual desktop
    /// or a suspended app, not tool windows, not the desktop or taskbar, at least 40 pixels each
    /// way. Bounds leave the shadow out.</summary>
    public static IReadOnlyList<PickableWindow> List()
    {
        using var pixels = new PerMonitorPixels();
        var windows = new List<PickableWindow>();
        EnumWindows((window, _) =>
        {
            if (Pickable(window) is { } picked) windows.Add(picked);
            return true;
        }, 0);
        return windows;
    }

    private static PickableWindow? Pickable(nint window)
    {
        if (!IsWindowVisible(window) || IsIconic(window)) return null;
        if (DwmGetWindowAttribute(window, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
        if ((GetWindowLongPtrW(window, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return null;
        var name = new char[256];
        var nameLength = GetClassNameW(window, name, name.Length);
        if (Desktop.Contains(new string(name, 0, nameLength))) return null;
        if (DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT bounds, Marshal.SizeOf<RECT>()) != 0
            && !GetWindowRect(window, out bounds))
            return null;
        if (bounds.Width < 40 || bounds.Height < 40) return null;
        var title = new char[512];
        var titleLength = GetWindowTextW(window, title, title.Length);
        return new PickableWindow(GdiScreenCapture.ToRect(bounds), new string(title, 0, titleLength));
    }
}
