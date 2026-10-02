using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Tinysnap.App;
using Tmds.DBus.Protocol;

namespace Tinysnap.Linux;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (DBusAddress.Session is not { } address)
        {
            Console.Error.WriteLine("Tinysnap needs a desktop session: no session bus was found.");
            return 1;
        }
        using var bus = AppBus.TryOwn(address).GetAwaiter().GetResult();
        if (bus is null)
        {
            // Opened again, from the menu or Open With: the running copy takes it from here.
            AppBus.HandOver(address, args).GetAwaiter().GetResult();
            return 0;
        }
        var settings = new GSettings();
        using var hotkeys = new GnomeShortcuts(settings);
        bus.Performed += hotkeys.Press;
        var desktop = DesktopEntries.ForThisUser();
        var wayland = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland";
        // Opened on first use, once Avalonia has made Xlib safe for threads.
        X11Screen? x11 = null;
        var x11Opened = false;
        X11Screen? Screen()
        {
            if (!x11Opened) (x11, x11Opened) = (X11Screen.TryOpen(), true);
            return x11;
        }
        // X11 needs a window to hold a copy; this one is never shown, so a copy works with none open.
        Window? holder = null;
        var platform = new LinuxPlatform(new LinuxScreenCapture(Screen, () => DBusConnection.Session, wayland), hotkeys,
            new LinuxClipboard(() => (holder ??= new Window()).Clipboard), bus, desktop, new LinuxFiles(settings));
        try
        {
            AppBuilder.Configure(() => new TinysnapApp(platform, Started, args))
                .UseX11()
                .With(new X11PlatformOptions { WmClass = "Tinysnap" })
                .UseSkia()
                .UseHarfBuzz()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            x11?.Dispose();
        }
        return 0;

        // The menu entry is written on every start, once assets can be read, so a moved AppImage
        // still opens from the menu.
        void Started(TinysnapApp app)
        {
            using var icon = AssetLoader.Open(new Uri("avares://Tinysnap.App/Assets/Tinysnap.png"));
            using var bytes = new MemoryStream();
            icon.CopyTo(bytes);
            desktop.Write(DesktopEntries.Exe, bytes.ToArray());
        }
    }
}
