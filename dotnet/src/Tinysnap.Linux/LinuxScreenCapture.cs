using SkiaSharp;
using Tinysnap.Core;
using Tinysnap.Platform;
using Tmds.DBus.Protocol;

namespace Tinysnap.Linux;

/// <summary>The frozen screen: on X11 read straight from the root window, on Wayland through
/// GNOME's screenshot portal, cut per monitor. Window picking on Wayland goes through GNOME's own
/// screenshot tool, since an app there cannot see other windows.</summary>
/// <param name="screen">X11, opened on first use: Xlib must not be called before Avalonia has
/// made it safe for threads.</param>
internal sealed class LinuxScreenCapture(Func<X11Screen?> screen, Func<DBusConnection> bus, bool wayland, string portal = "org.freedesktop.portal.Desktop")
    : IScreenCapture
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    /// <summary>Image pixels per X11 pixel in the last Wayland freeze, 2 on a 2x desktop.</summary>
    private double factor = 1;

    /// <summary>The primary monitor in the last freeze, in its image's pixels.</summary>
    private Rect? primary;

    public FrozenDesktop Freeze()
    {
        if (screen() is not { } x11) return new FrozenDesktop([], []);
        var monitors = x11.Monitors();
        if (!wayland)
            return new FrozenDesktop([.. monitors.Select(m => x11.Grab(m.Bounds) is { } image ? new FrozenScreen(m.Bounds, x11.Scale, image) : null).OfType<FrozenScreen>()],
                                     x11.Windows());
        // ponytail: blocks the UI thread until GNOME answers, the person's first Allow included;
        // an asynchronous Freeze across the platforms if that wait ever matters. Run on the pool,
        // so the portal's replies never wait for the blocked UI thread.
        var path = Task.Run(() => Portal.Screenshot(bus(), interactive: false, Wait, portal)).GetAwaiter().GetResult();
        if (path is null || Decode(path) is not { } desktop) return new FrozenDesktop([], []);
        var screens = DesktopCut.Cut(desktop, monitors, x11.Scale);
        // A monitor covering the whole picture is given the picture itself, the very same object,
        // which must outlive this: disposed, the overlay drew nothing.
        if (!screens.Any(screen => ReferenceEquals(screen.Image, desktop))) desktop.Dispose();
        factor = screens[0].Scale / x11.Scale;
        var main = monitors.Select((m, i) => (m.Primary, i)).FirstOrDefault(m => m.Primary).i;
        primary = screens[Math.Min(main, screens.Count - 1)].Bounds;
        return new FrozenDesktop(screens, []);
    }

    /// <summary>On Wayland the pointer is unknown outside Tinysnap's own windows, so a fullscreen
    /// capture takes the primary monitor, whose centre this is.</summary>
    public Point PointerPosition()
    {
        var x11 = screen();
        if (!wayland && x11?.Pointer() is { } pointer) return pointer;
        var main = primary ?? x11?.Monitors().OrderByDescending(m => m.Primary).FirstOrDefault().Bounds ?? Rect.Zero;
        return new Point(main.X + main.Width / 2, main.Y + main.Height / 2);
    }

    /// <summary>GNOME's windows round their corners by 12 points.</summary>
    public double WindowCornerRadius => 12;

    public Func<Task<Capture?>>? PickWindow => wayland ? PickWithGnome : null;

    /// <summary>GNOME moves a window placed off screen back into view, and one over its top bar below it.</summary>
    public bool PlacesWindowsAsAsked => false;

    public void Focus(nint window)
    {
        if (window != 0) screen()?.Activate((nuint)window);
    }

    /// <summary>Nothing an app can do: GNOME Shell zooms in every normal window as it maps, and Mutter
    /// lets only a normal window go full screen, which the overlay needs to cover the top bar. The
    /// person can turn it off in Settings, Accessibility, Reduce Animation.</summary>
    public void ShowAtOnce(nint window) { }

    /// <summary>The window GNOME's tool picked, at the scale of the last freeze.</summary>
    // ponytail: the scale comes from the last full freeze, 1 before any; Mutter's DisplayConfig
    // gives it directly if a picked window ever opens at the wrong size.
    private async Task<Capture?> PickWithGnome()
    {
        if (await Portal.Screenshot(bus(), interactive: true, TimeSpan.FromMinutes(5), portal) is not { } path) return null;
        return Decode(path) is { } image ? new Capture(image, (screen()?.Scale ?? 1) * factor) : null;
    }

    /// <summary>The portal's picture decoded now, as every capture is held in memory, and its file
    /// removed, since it is Tinysnap's to clear up.</summary>
    private static SKImage? Decode(string path)
    {
        try
        {
            using var encoded = SKImage.FromEncodedData(path);
            return encoded?.ToRasterImage(ensurePixelData: true);
        }
        finally { TryDelete(path); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
