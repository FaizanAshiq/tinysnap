using Avalonia;
using Tinysnap.App;
using Tinysnap.Platform;

namespace Tinysnap.Windows;

internal sealed class WindowsPlatform(IScreenCapture screen, IHotkeys hotkeys, IClipboard clipboard) : IPlatform
{
    public IScreenCapture Screen { get; } = screen;
    public IHotkeys Hotkeys { get; } = hotkeys;
    public IClipboard Clipboard { get; } = clipboard;
    public IFileActions Files { get; } = new Win32Files();
    public IStartup Startup { get; } = new Win32Startup();

    /// <summary>Settings, Accessibility, Visual effects, Animation effects off.</summary>
    public bool ReduceMotion =>
        Native.SystemParametersInfoW(Native.SPI_GETCLIENTAREAANIMATION, 0, out var animate, 0) && !animate;
}

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Made here, on the thread that becomes the UI thread, whose message loop delivers the hotkeys.
        using var hotkeys = new Win32Hotkeys();
        using var clipboard = new Win32Clipboard();
        var platform = new WindowsPlatform(new GdiScreenCapture(), hotkeys, clipboard);
        AppBuilder.Configure(() => new TinysnapApp(platform))
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
