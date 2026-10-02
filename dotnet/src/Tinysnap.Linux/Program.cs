using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using SkiaSharp;
using Tinysnap.App;
using Tmds.DBus.Protocol;
using Velopack;

namespace Tinysnap.Linux;

internal static class Program
{
    /// <summary>Avalonia on X11, which XWayland gives Wayland sessions too.</summary>
    private static AppBuilder X11(AppBuilder builder)
    {
        var options = new X11PlatformOptions { WmClass = "Tinysnap" };
        // With no graphics device, OpenGL is Mesa's software renderer, which Avalonia turns down
        // only after building it: seconds on a first start. Skia draws in software straight away.
        if (!Directory.Exists("/dev/dri") || !Directory.EnumerateFileSystemEntries("/dev/dri", "renderD*").Any())
            options.RenderingMode = [X11RenderingMode.Software];
        builder = builder.UseX11().With(options).UseSkia().UseHarfBuzz();
        // Avalonia's default font is Skia's, whichever font fontconfig ranks first: with
        // Ghostscript's fonts installed that was C059, a serif face with no Medium, and the first
        // editor crashed. The desktop's own sans-serif is what other apps show.
        using var sans = SKFontManager.Default.MatchFamily("sans-serif");
        return sans?.FamilyName is { Length: > 0 } family ? builder.With(new FontManagerOptions { DefaultFamilyName = family }) : builder;
    }

    public static int Main(string[] args)
    {
        // First of all, as the AppImage's runtime expects. No update is ever checked for.
        VelopackApp.Build().Run();
        if (DBusAddress.Session is not { } address)
        {
            Console.Error.WriteLine("Tinysnap needs a desktop session: no session bus was found.");
            return 1;
        }
        if (args is ["--self-check", var report])
        {
            // A name of its own, so a running Tinysnap is neither reached nor in the way.
            using var checkBus = AppBus.TryOwn(address, $"com.faizanashiq.TinysnapSelfCheck{Guid.NewGuid():N}").GetAwaiter().GetResult()!;
            var gnome = new GSettings();
            return SelfCheck.Run(report, () => new LinuxPlatform(new LinuxScreenCapture(() => null, Portal.Connection(address), wayland: false),
                                                                 new GnomeShortcuts(gnome), new LinuxClipboard(() => null), checkBus,
                                                                 DesktopEntries.ForThisUser(), new LinuxFiles(gnome)),
                                 X11, textRequired: true);
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
        // Logging out ends Tinysnap with SIGTERM, and closing its terminal with SIGHUP, which end it
        // at once, skipping Avalonia's shutdown and even .NET's exit handlers. Caught here, GNOME
        // gets Print Screen back and loses Tinysnap's shortcuts before the process goes.
        void GiveBack(PosixSignalContext signal)
        {
            Console.Error.WriteLine($"tinysnap: ended by {signal.Signal}, giving GNOME its shortcuts back");
            hotkeys.UnregisterAll();
        }
        using var ended = PosixSignalRegistration.Create(PosixSignal.SIGTERM, GiveBack);
        using var hungUp = PosixSignalRegistration.Create(PosixSignal.SIGHUP, GiveBack);
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
        var platform = new LinuxPlatform(new LinuxScreenCapture(Screen, Portal.Connection(address), wayland), hotkeys,
            new LinuxClipboard(() => (holder ??= new Window()).Clipboard), bus, desktop, new LinuxFiles(settings));
        try
        {
            X11(AppBuilder.Configure(() => new TinysnapApp(platform, Started, args)))
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
            // One line in the session log, for when a start feels slow.
            var (runs, time) = Commands.Spent;
            var ready = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
            Console.Error.WriteLine($"tinysnap: ready {ready.TotalMilliseconds:0} ms after starting, "
                                    + $"{time.TotalMilliseconds:0} ms of it in {runs} runs of GNOME's tools");
            using var icon = AssetLoader.Open(new Uri("avares://Tinysnap.App/Assets/Tinysnap.png"));
            using var bytes = new MemoryStream();
            icon.CopyTo(bytes);
            // Off the UI thread: rebuilding the desktop database takes a moment the first capture should not wait for.
            var png = bytes.ToArray();
            _ = Task.Run(() => desktop.Write(DesktopEntries.Exe, png));
        }
    }
}
