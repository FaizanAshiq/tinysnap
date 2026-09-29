using Avalonia;
using Tinysnap.App;
using Tinysnap.Platform;

namespace Tinysnap.Windows;

internal sealed class WindowsPlatform(IScreenCapture screen, IHotkeys hotkeys) : IPlatform
{
    public IScreenCapture Screen { get; } = screen;
    public IHotkeys Hotkeys { get; } = hotkeys;
}

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Made here, on the thread that becomes the UI thread, whose message loop delivers the hotkeys.
        using var hotkeys = new Win32Hotkeys();
        var platform = new WindowsPlatform(new GdiScreenCapture(), hotkeys);
        AppBuilder.Configure(() => new TinysnapApp(platform))
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
