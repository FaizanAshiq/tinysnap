using System.Text.RegularExpressions;
using Tinysnap.Platform;

namespace Tinysnap.Linux;

/// <summary>What an installer would have done, done by Tinysnap itself, since an AppImage has
/// none: the menu entry with its icon, which also puts Tinysnap in Files' Open With for PNG and
/// JPEG, the autostart entry for start at login, and taking them all away again.</summary>
internal sealed partial class DesktopEntries(string dataHome, string configHome, Func<string, string[], (int Exit, string Output)>? run = null)
    : IStartup
{
    private const string Id = "com.faizanashiq.Tinysnap";
    private readonly Func<string, string[], (int Exit, string Output)> run = run ?? Commands.Run;
    private string? exe;

    private string Menu => Path.Combine(dataHome, "applications", $"{Id}.desktop");
    private string Icon => Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", $"{Id}.png");
    private string Autostart => Path.Combine(configHome, "autostart", $"{Id}.desktop");

    /// <summary>The AppImage when running from one, as its runtime says, else this program.</summary>
    public static string Exe => Environment.GetEnvironmentVariable("APPIMAGE") ?? Environment.ProcessPath!;

    public static DesktopEntries ForThisUser()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(home, ".local", "share"),
                   Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"));
    }

    /// <summary>Written on every start, so moving the AppImage moves the entries with it.</summary>
    public void Write(string exe, byte[] icon)
    {
        this.exe = exe;
        Directory.CreateDirectory(Path.GetDirectoryName(Icon)!);
        File.WriteAllBytes(Icon, icon);
        Directory.CreateDirectory(Path.GetDirectoryName(Menu)!);
        File.WriteAllText(Menu, Entry(exe, "%F"));
        if (File.Exists(Autostart)) File.WriteAllText(Autostart, Entry(exe, null) + "X-GNOME-Autostart-enabled=true\n");
        // Files learns of the new MIME types from the cache this rebuilds.
        run("update-desktop-database", [Path.GetDirectoryName(Menu)!]);
    }

    public bool IsEnabled => File.Exists(Autostart);

    public bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(Autostart)) File.Delete(Autostart);
                return true;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Autostart)!);
            File.WriteAllText(Autostart, Entry(exe ?? Exe, null) + "X-GNOME-Autostart-enabled=true\n");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The menu entry, its icon and the autostart entry gone; the person deletes the AppImage.</summary>
    public void Remove()
    {
        foreach (var file in new[] { Menu, Icon, Autostart })
            if (File.Exists(file)) File.Delete(file);
        if (Directory.Exists(Path.GetDirectoryName(Menu))) run("update-desktop-database", [Path.GetDirectoryName(Menu)!]);
    }

    private static string Entry(string exe, string? files) => $"""
        [Desktop Entry]
        Type=Application
        Name=Tinysnap
        Comment=Capture, mark up and pin screenshots
        Keywords=screenshot;capture;annotate;
        Exec="{Quoted(exe)}"{(files is null ? "" : " " + files)}
        Icon={Id}
        Terminal=false
        MimeType=image/png;image/jpeg;
        Categories=Graphics;Utility;
        StartupWMClass=Tinysnap

        """;

    /// <summary>A path inside the Exec key's quotes, as the desktop entry spec has it: <c>"</c>,
    /// <c>`</c>, <c>$</c> and <c>\</c> escaped, every backslash doubled again because the key is a
    /// string, and <c>%</c> written <c>%%</c> so it is not taken for a field code.</summary>
    private static string Quoted(string path) =>
        Reserved().Replace(path, @"\$0").Replace(@"\", @"\\").Replace("%", "%%");

    [GeneratedRegex(@"[""`$\\]")]
    private static partial Regex Reserved();
}
