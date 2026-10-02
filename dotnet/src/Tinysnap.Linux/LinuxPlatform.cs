using Tinysnap.Platform;

namespace Tinysnap.Linux;

internal sealed class LinuxPlatform(IScreenCapture screen, GnomeShortcuts hotkeys, IClipboard clipboard, AppBus bus,
                                    DesktopEntries desktop, LinuxFiles files) : IPlatform
{
    public IScreenCapture Screen { get; } = screen;
    public IHotkeys Hotkeys { get; } = hotkeys;
    public IClipboard Clipboard { get; } = clipboard;
    public IFileActions Files { get; } = files;
    public IStartup Startup { get; } = desktop;
    public ITextReader Text { get; } = new LinuxTextReader();

    public event Action<IReadOnlyList<string>>? Reopened
    {
        add => bus.Opened += value;
        remove => bus.Opened -= value;
    }

    public bool ReduceMotion => files.ReduceMotion;

    /// <summary>GNOME's top bar is dark in both styles, so the icon stays white.</summary>
    public bool LightTaskbar => false;

    /// <summary>The shortcuts, the menu entry, Open With and start at login; Settings then quits.</summary>
    public Action? RemoveFromComputer => () =>
    {
        hotkeys.UnregisterAll();
        desktop.Remove();
    };
}
