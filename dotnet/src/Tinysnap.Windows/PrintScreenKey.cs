using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Tinysnap.Windows;

/// <summary>Windows 11 hands Print Screen to the Snipping Tool while "Use the Print screen key to
/// open screen capture" is on, or was never set, and then no app can register the key. While
/// Tinysnap holds Print Screen the setting is off, and it goes back exactly as it was when
/// Tinysnap lets the key go, so the Snipping Tool has it again whenever Tinysnap is not running.</summary>
internal sealed class PrintScreenKey(string keyboard = @"Control Panel\Keyboard", string own = @"Software\Tinysnap")
{
    private const string Setting = "PrintScreenKeyForSnippingEnabled";

    /// <summary>What the setting held before Tinysnap first turned it off, kept in Tinysnap's own
    /// key until it goes back, so a copy killed while holding the key is put right by the next.</summary>
    private const string Before = "PrintScreenKeyBefore";
    private const string Unset = "unset";

    public void Take()
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyboard);
        using var mine = Registry.CurrentUser.CreateSubKey(own);
        var value = key.GetValue(Setting);
        if (mine.GetValue(Before) is null)
        {
            if (value is 0) return;
            mine.SetValue(Before, value ?? Unset);
        }
        if (value is 0) return;
        key.SetValue(Setting, 0, RegistryValueKind.DWord);
        Announce();
    }

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

    /// <summary>Tells every window a setting changed, as Settings does, so the shell reads it again.</summary>
    private static void Announce() => SendMessageTimeoutW(0xFFFF, 0x001A, 0, null, 0x0002, 1000, out _);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(nint window, uint message, nint wParam, string? lParam, uint flags,
                                                   uint timeout, out nint result);
}
