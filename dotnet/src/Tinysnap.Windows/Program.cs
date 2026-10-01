using Avalonia;
using Tinysnap.App;
using Tinysnap.Platform;
using Velopack;

namespace Tinysnap.Windows;

internal sealed class WindowsPlatform(IScreenCapture screen, IHotkeys hotkeys, IClipboard clipboard, SingleInstance instance)
    : IPlatform
{
    public IScreenCapture Screen { get; } = screen;
    public IHotkeys Hotkeys { get; } = hotkeys;
    public IClipboard Clipboard { get; } = clipboard;
    public IFileActions Files { get; } = new Win32Files();
    public IStartup Startup { get; } = new Win32Startup();
    public ITextReader Text { get; } = new WinRtTextReader();

    public event Action<IReadOnlyList<string>>? Reopened
    {
        add => instance.Reopened += value;
        remove => instance.Reopened -= value;
    }

    /// <summary>Settings, Personalisation, Colours: the mode for Windows, which the taskbar follows,
    /// light on Windows 11 out of the box.</summary>
    public bool LightTaskbar =>
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                                          "SystemUsesLightTheme", 0) is 1;

    /// <summary>Settings, Accessibility, Visual effects, Animation effects off.</summary>
    public bool ReduceMotion =>
        Native.SystemParametersInfoW(Native.SPI_GETCLIENTAREAANIMATION, 0, out var animate, 0) && !animate;
}

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // First of all: the installer runs the exe to install and uninstall, and those runs end
        // here. No update is ever checked for; winget brings new versions.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => Installed(new Win32FileTypes()))
            .OnAfterUpdateFastCallback(_ => Installed(new Win32FileTypes()))
            .OnBeforeUninstallFastCallback(_ => Uninstalling(new Win32Startup(), new Win32FileTypes()))
            .Run();
        if (args is ["--self-check", var report])
        {
            Environment.ExitCode = SelfCheck.Run(report);
            return;
        }
        using var instance = new SingleInstance();
        if (!instance.IsFirst)
        {
            // Opened again, from "Open with" or the Start menu: the running copy takes it from here.
            instance.AskFirstToReopen(args);
            return;
        }
        // Made here, on the thread that becomes the UI thread, whose message loop delivers the hotkeys.
        using var hotkeys = new Win32Hotkeys();
        using var clipboard = new Win32Clipboard();
        var platform = new WindowsPlatform(new GdiScreenCapture(), hotkeys, clipboard, instance);
        _ = TrayPromotion.PromoteSoon(Environment.ProcessPath!);
        AppBuilder.Configure(() => new TinysnapApp(platform, files: args))
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Installing, or updating to a new place, puts Tinysnap in "Open with" for pictures.</summary>
    internal static void Installed(Win32FileTypes types) => types.Register(Environment.ProcessPath!);

    /// <summary>Uninstalling leaves no start-at-login entry, and no "Open with" entry, pointing at a
    /// removed exe, and gives Print Screen back to the Snipping Tool if a copy killed while holding
    /// it never did.</summary>
    internal static void Uninstalling(IStartup startup, Win32FileTypes types, PrintScreenKey? printScreen = null)
    {
        startup.SetEnabled(false);
        types.Unregister();
        (printScreen ?? new PrintScreenKey()).GiveBack();
    }
}
