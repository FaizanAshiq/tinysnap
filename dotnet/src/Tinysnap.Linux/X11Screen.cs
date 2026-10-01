using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Tinysnap.Platform;
using Point = Tinysnap.Core.Point;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.Linux;

/// <summary>An X11 session read directly: its monitors, the pointer, pixels from the root window,
/// and the window manager's windows. On Wayland only XWayland's own windows are visible here,
/// so the screen and window list come from GNOME instead.</summary>
/// <remarks>ponytail: an X error, such as a window closing while it is listed, goes to the
/// process's handler, which Avalonia sets to record rather than exit; own handler if this is
/// ever used before Avalonia starts.</remarks>
internal sealed unsafe class X11Screen : IDisposable
{
    private readonly nint display;
    private readonly nuint root;

    private X11Screen(nint display)
    {
        this.display = display;
        root = X11.XDefaultRootWindow(display);
    }

    public static X11Screen? TryOpen()
    {
        try
        {
            var display = X11.XOpenDisplay(0);
            return display == 0 ? null : new X11Screen(display);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Each monitor in X11 pixels, or the whole root window where RandR reports none.</summary>
    public IReadOnlyList<(Rect Bounds, bool Primary)> Monitors()
    {
        var monitors = X11.XRRGetMonitors(display, root, 1, out var count);
        try
        {
            if (monitors is not null && count > 0)
                return [.. Enumerable.Range(0, count).Select(i => (new Rect(monitors[i].X, monitors[i].Y, monitors[i].Width, monitors[i].Height), monitors[i].Primary != 0))];
        }
        finally
        {
            if (monitors is not null) X11.XRRFreeMonitors(monitors);
        }
        return [(Whole, true)];
    }

    private Rect Whole => X11.XGetWindowAttributes(display, root, out var attributes) != 0
        ? new Rect(0, 0, attributes.Width, attributes.Height)
        : Rect.Zero;

    /// <summary>The desktop's scale from Xft.dpi, which GNOME sets to 96 times the scale.</summary>
    public double Scale
    {
        get
        {
            var resources = Marshal.PtrToStringUTF8(X11.XResourceManagerString(display)) ?? "";
            var line = resources.Split('\n').FirstOrDefault(l => l.StartsWith("Xft.dpi:"));
            return line is not null && double.TryParse(line["Xft.dpi:".Length..].Trim(), CultureInfo.InvariantCulture, out var dpi) && dpi > 0 ? dpi / 96 : 1;
        }
    }

    public Point? Pointer() =>
        X11.XQueryPointer(display, root, out _, out _, out var x, out var y, out _, out _, out _) != 0 ? new Point(x, y) : null;

    /// <summary>Pixels of the root window, which an X11 compositor has composed; blue first, made
    /// opaque. Cut to the screen, since X refuses a grab past its edge; null when nothing is left.</summary>
    public SKImage? Grab(Rect bounds)
    {
        var whole = Whole;
        int left = (int)Math.Max(bounds.X, 0), top = (int)Math.Max(bounds.Y, 0);
        int right = (int)Math.Min(bounds.X + bounds.Width, whole.Width), bottom = (int)Math.Min(bounds.Y + bounds.Height, whole.Height);
        if (right <= left || bottom <= top) return null;
        var image = X11.XGetImage(display, root, left, top, (uint)(right - left), (uint)(bottom - top), nuint.MaxValue, X11.ZPixmap);
        if (image == 0) return null;
        try
        {
            if (X11.ImageBitsPerPixel(image) != 32) return null;
            int width = X11.ImageWidth(image), height = X11.ImageHeight(image), stride = X11.ImageBytesPerLine(image);
            var data = X11.ImageData(image);
            for (var y = 0; y < height; y++)
            {
                var row = new Span<uint>((void*)(data + y * stride), width);
                for (var x = 0; x < width; x++) row[x] |= 0xFF000000;
            }
            return SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque), data, stride);
        }
        finally { X11.DestroyImage(image); }
    }

    /// <summary>Normal windows the window manager shows, front to back, with their title bar and
    /// without the shadow a client draws round itself.</summary>
    public IReadOnlyList<PickableWindow> Windows()
    {
        var stacking = Property(root, "_NET_CLIENT_LIST_STACKING");
        var windows = new List<PickableWindow>();
        foreach (var window in stacking.AsEnumerable().Reverse())
        {
            if (X11.XGetWindowAttributes(display, window, out var attributes) == 0 || attributes.MapState != X11.IsViewable) continue;
            if (Property(window, "_NET_WM_STATE").Contains(Atom("_NET_WM_STATE_HIDDEN"))) continue;
            var type = Property(window, "_NET_WM_WINDOW_TYPE");
            if (type.Length > 0 && !type.Contains(Atom("_NET_WM_WINDOW_TYPE_NORMAL")) && !type.Contains(Atom("_NET_WM_WINDOW_TYPE_DIALOG"))) continue;
            X11.XTranslateCoordinates(display, window, root, 0, 0, out var x, out var y, out _);
            var frame = Property(window, "_NET_FRAME_EXTENTS");
            var shadow = Property(window, "_GTK_FRAME_EXTENTS");
            double left = x, top = y, right = x + attributes.Width, bottom = y + attributes.Height;
            if (frame.Length == 4) (left, right, top, bottom) = (left - frame[0], right + frame[1], top - frame[2], bottom + frame[3]);
            if (shadow.Length == 4) (left, right, top, bottom) = (left + shadow[0], right - shadow[1], top + shadow[2], bottom - shadow[3]);
            windows.Add(new PickableWindow(new Rect(left, top, right - left, bottom - top), Title(window)));
        }
        return windows;
    }

    private nuint Atom(string name) => X11.XInternAtom(display, name, 0);

    /// <summary>A property of 32 bit items, which Xlib hands back as longs.</summary>
    private nuint[] Property(nuint window, string name)
    {
        if (X11.XGetWindowProperty(display, window, Atom(name), 0, 1024, 0, 0, out _, out var format, out var items, out _, out var data) != 0 || data == 0)
            return [];
        try
        {
            return format == 32 ? new ReadOnlySpan<nuint>((void*)data, (int)items).ToArray() : [];
        }
        finally { X11.XFree(data); }
    }

    private string Title(nuint window)
    {
        foreach (var name in new[] { "_NET_WM_NAME", "WM_NAME" })
        {
            if (X11.XGetWindowProperty(display, window, Atom(name), 0, 1024, 0, 0, out _, out var format, out var items, out _, out var data) != 0 || data == 0)
                continue;
            try
            {
                if (format == 8 && items > 0) return Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)data, (int)items));
            }
            finally { X11.XFree(data); }
        }
        return "";
    }

    public void Dispose() => X11.XCloseDisplay(display);
}
