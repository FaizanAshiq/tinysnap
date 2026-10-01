using Microsoft.Win32;

namespace Tinysnap.Windows;

/// <summary>Windows 11 puts a new notification icon behind the arrow in the taskbar. Tinysnap's
/// is brought out once, as the person would by dragging it; an icon they hide again stays hidden.</summary>
internal static class TrayPromotion
{
    private const string Icons = @"Control Panel\NotifyIconSettings";

    /// <summary>True once Windows has an entry for <paramref name="exe"/>, promoted now or left as
    /// the person set it. False while there is none, as for a moment after the icon first shows.</summary>
    public static bool Promote(string exe, string icons = Icons)
    {
        using var all = Registry.CurrentUser.OpenSubKey(icons);
        if (all is null) return false;
        foreach (var id in all.GetSubKeyNames())
        {
            using var icon = Registry.CurrentUser.OpenSubKey($@"{icons}\{id}", writable: true);
            if (icon?.GetValue("ExecutablePath") is not string path || !string.Equals(path, exe, StringComparison.OrdinalIgnoreCase))
                continue;
            if (icon.GetValue("IsPromoted") is null) icon.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
            return true;
        }
        return false;
    }

    /// <summary>Promotes the icon once Windows has made its entry, looking for ten seconds after start.</summary>
    public static async Task PromoteSoon(string exe)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (Promote(exe)) return;
            await Task.Delay(500);
        }
    }
}
