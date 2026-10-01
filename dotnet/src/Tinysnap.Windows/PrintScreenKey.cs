using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Tinysnap.Windows;

/// <summary>Windows 11 hands Print Screen to the Snipping Tool while "Use the Print screen key to
/// open screen capture" is on, or was never set, and then no app can register the key. While
/// Tinysnap holds Print Screen the setting is off, and it goes back exactly as it was when
/// Tinysnap lets the key go, so the Snipping Tool has it again whenever Tinysnap is not running.</summary>
internal sealed class PrintScreenKey(string keyboard = @"Control Panel\Keyboard")
{
    private const string Setting = "PrintScreenKeyForSnippingEnabled";

    /// <summary>What the setting held before Tinysnap turned it off, null while Tinysnap has not.</summary>
    private object? before;
    private bool taken;

    public void Take()
    {
        if (taken) return;
        using var key = Registry.CurrentUser.CreateSubKey(keyboard);
        var value = key.GetValue(Setting);
        if (value is 0) return;
        key.SetValue(Setting, 0, RegistryValueKind.DWord);
        (before, taken) = (value, true);
        Announce();
    }

    public void GiveBack()
    {
        if (!taken) return;
        using var key = Registry.CurrentUser.CreateSubKey(keyboard);
        if (before is null) key.DeleteValue(Setting, throwOnMissingValue: false);
        else key.SetValue(Setting, before, RegistryValueKind.DWord);
        (before, taken) = (null, false);
        Announce();
    }

    /// <summary>Tells every window a setting changed, as Settings does, so the shell reads it again.</summary>
    private static void Announce() => SendMessageTimeoutW(0xFFFF, 0x001A, 0, null, 0x0002, 1000, out _);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(nint window, uint message, nint wParam, string? lParam, uint flags,
                                                   uint timeout, out nint result);
}
