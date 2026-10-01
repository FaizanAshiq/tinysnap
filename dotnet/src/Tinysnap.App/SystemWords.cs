namespace Tinysnap.App;

/// <summary>The words each system uses for its own things, so the app speaks as the desktop does.</summary>
internal static class SystemWords
{
    public static string Bin => OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash";

    public static string FileManager => OperatingSystem.IsWindows() ? "Explorer" : OperatingSystem.IsMacOS() ? "Finder" : "Files";

    public static string Os => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "The system";

    /// <summary>Where the icon sits: the taskbar's notification area, or GNOME's top bar.</summary>
    public static string TrayIcon => OperatingSystem.IsLinux() ? "top bar icon" : "tray icon";
}
