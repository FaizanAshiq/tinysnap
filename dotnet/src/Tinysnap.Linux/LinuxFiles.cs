using SkiaSharp;
using Tinysnap.Platform;

namespace Tinysnap.Linux;

/// <summary>Files' side of GNOME: the Trash through <c>gio</c>, Files opened on an item, links in
/// the browser, the desktop picture and the animations setting.</summary>
internal sealed class LinuxFiles(GSettings settings, Func<string, string[], (int Exit, string Output)>? run = null) : IFileActions
{
    private const string Background = "org.gnome.desktop.background";
    private const string Interface = "org.gnome.desktop.interface";
    private readonly Func<string, string[], (int Exit, string Output)> run = run ?? Commands.Run;

    public bool MoveToRecycleBin(string path) => run("gio", ["trash", path]).Exit == 0;

    /// <summary>Files on the folder with the item selected, through the file manager's own D-Bus
    /// call, or just the folder where no file manager answers it.</summary>
    public void Reveal(string path)
    {
        var shown = run("gdbus", ["call", "--session", "--dest", "org.freedesktop.FileManager1", "--object-path", "/org/freedesktop/FileManager1",
                                  "--method", "org.freedesktop.FileManager1.ShowItems", $"[{GSettings.Quote(new Uri(path).AbsoluteUri)}]", "''"]);
        if (shown.Exit != 0) run("gio", ["open", Path.GetDirectoryName(path)!]);
    }

    public void Open(Uri link) => run("gio", ["open", link.AbsoluteUri]);

    /// <summary>The picture for the current style, the light one when a dark style has none.</summary>
    public SKImage? Wallpaper()
    {
        var dark = settings.GetStrings(Interface, "color-scheme") is ["prefer-dark"];
        string[] keys = dark ? ["picture-uri-dark", "picture-uri"] : ["picture-uri"];
        foreach (var key in keys)
        {
            if (settings.GetStrings(Background, key) is not [var uri]) continue;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile || !File.Exists(parsed.LocalPath)) continue;
            // A slideshow is an XML file, which reads as no picture.
            return SKImage.FromEncodedData(parsed.LocalPath);
        }
        return null;
    }

    public bool ReduceMotion => settings.Get(Interface, "enable-animations") == "false";
}
