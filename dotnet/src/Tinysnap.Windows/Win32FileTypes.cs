using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Tinysnap.Windows;

/// <summary>Tinysnap in Explorer's "Open with" for pictures, per user, written when it is
/// installed and removed when it is uninstalled. It never becomes the default app for a type:
/// that stays the person's choice.</summary>
internal sealed class Win32FileTypes(string progId = "Tinysnap.Picture", params string[] extensions)
{
    private const string Classes = @"Software\Classes";

    private readonly string[] extensions = extensions.Length > 0 ? extensions : [".png", ".jpg", ".jpeg"];

    public void Register(string exe)
    {
        using (var type = Registry.CurrentUser.CreateSubKey($@"{Classes}\{progId}"))
        {
            type.SetValue("", "Tinysnap picture");
            using var icon = type.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exe}\",0");
            using var command = type.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{exe}\" \"%1\"");
        }
        foreach (var extension in extensions)
        {
            using var openWith = Registry.CurrentUser.CreateSubKey($@"{Classes}\{extension}\OpenWithProgids");
            openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
        }
        Announce();
    }

    public void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"{Classes}\{progId}", throwOnMissingSubKey: false);
        foreach (var extension in extensions)
        {
            using var openWith = Registry.CurrentUser.OpenSubKey($@"{Classes}\{extension}\OpenWithProgids", writable: true);
            openWith?.DeleteValue(progId, throwOnMissingValue: false);
        }
        Announce();
    }

    public bool IsRegisteredFor(string extension)
    {
        using var openWith = Registry.CurrentUser.OpenSubKey($@"{Classes}\{extension}\OpenWithProgids");
        return openWith?.GetValueNames().Contains(progId) == true;
    }

    /// <summary>Tells Explorer the associations changed, so its menus show it at once.</summary>
    private static void Announce() => SHChangeNotify(0x08000000, 0, 0, 0);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
