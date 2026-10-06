using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Tinysnap.Windows;

/// <summary>Up to 1.4.4, Tinysnap turned "Use the Print screen key to open screen capture" off while
/// it held Print Screen and kept what the setting held in its own key, to put back on quitting. A copy
/// killed first left it off. Tinysnap no longer touches the setting, since a keyboard hook takes the key
/// whatever it says, so this puts back what an earlier copy left, once, at start and on uninstalling.</summary>
internal sealed class PrintScreenKey(string keyboard = @"Control Panel\Keyboard", string own = @"Software\Tinysnap")
{
    private const string Setting = "PrintScreenKeyForSnippingEnabled";

    /// <summary>What the setting held before an earlier Tinysnap turned it off.</summary>
    private const string Before = "PrintScreenKeyBefore";
    private const string Unset = "unset";

    public void GiveBack()
    {
        using var mine = Registry.CurrentUser.OpenSubKey(own, writable: true);
        if (mine?.GetValue(Before) is not { } before) return;
        using var key = Registry.CurrentUser.CreateSubKey(keyboard);
        if (before is Unset) key.DeleteValue(Setting, throwOnMissingValue: false);
        else key.SetValue(Setting, before, RegistryValueKind.DWord);
        mine.DeleteValue(Before, throwOnMissingValue: false);
        Announce();
    }

    /// <summary>Tells every window a setting changed; Explorer itself reads this one again only when it
    /// restarts, so the Settings switch shows it at once and the key follows at the next sign in.</summary>
    private static void Announce() => SendMessageTimeoutW(0xFFFF, 0x001A, 0, null, 0x0002, 1000, out _);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(nint window, uint message, nint wParam, string? lParam, uint flags,
                                                   uint timeout, out nint result);
}
