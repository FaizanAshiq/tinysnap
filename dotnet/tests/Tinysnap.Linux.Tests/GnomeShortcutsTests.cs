using Tinysnap.Core;

namespace Tinysnap.Linux.Tests;

public class GnomeShortcutsTests
{
    private const string MediaKeys = "org.gnome.settings-daemon.plugins.media-keys";
    private const string Path = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/";
    private const string Custom = "org.gnome.settings-daemon.plugins.media-keys.custom-keybinding";
    private const string Shell = "org.gnome.shell.keybindings";

    private readonly FakeGSettings fake = new();
    private readonly string state = Directory.CreateTempSubdirectory().FullName;

    private GnomeShortcuts Shortcuts() => new(new GSettings(fake.Run), stateFolder: state);

    [Fact]
    public void AHotkeyBecomesACustomShortcutBesideThePersonsOwn()
    {
        fake.Values[$"{MediaKeys}|custom-keybindings"] = $"['{Path}custom0/']";
        fake.Values[$"{Custom}:{Path}custom0/|binding"] = "'<Super>t'";
        using var shortcuts = Shortcuts();
        Assert.True(shortcuts.Register(HotKeyAction.Fullscreen, new HotKeyBinding(0x31, [ModifierKey.Control, ModifierKey.Shift])));
        Assert.Equal($"['{Path}custom0/', '{Path}tinysnap-fullscreen/']", fake.Values[$"{MediaKeys}|custom-keybindings"]);
        Assert.Equal("'<Shift><Control>1'", fake.Values[$"{Custom}:{Path}tinysnap-fullscreen/|binding"]);
        Assert.Contains("Perform fullscreen", fake.Values[$"{Custom}:{Path}tinysnap-fullscreen/|command"]);

        shortcuts.UnregisterAll();
        Assert.Equal($"['{Path}custom0/']", fake.Values[$"{MediaKeys}|custom-keybindings"]);
        Assert.False(fake.Values.ContainsKey($"{Custom}:{Path}tinysnap-fullscreen/|binding"));
        Assert.Equal("'<Super>t'", fake.Values[$"{Custom}:{Path}custom0/|binding"]);
    }

    [Fact]
    public void ACombinationGnomeOrThePersonUsesIsTaken()
    {
        fake.Values["org.gnome.desktop.wm.keybindings|switch-to-workspace-1"] = "['<Super>Home', '<Primary><Shift>1']";
        fake.Values[$"{MediaKeys}|custom-keybindings"] = $"['{Path}custom0/']";
        fake.Values[$"{Custom}:{Path}custom0/|binding"] = "'<Alt>o'";
        using var shortcuts = Shortcuts();
        Assert.False(shortcuts.Register(HotKeyAction.Fullscreen, new HotKeyBinding(0x31, [ModifierKey.Shift, ModifierKey.Control])));
        Assert.False(shortcuts.Register(HotKeyAction.Text, new HotKeyBinding(0x4F, [ModifierKey.Alt])));
        Assert.True(shortcuts.Register(HotKeyAction.Qr, new HotKeyBinding(0x52, [ModifierKey.Alt])));
    }

    [Fact]
    public void PrintScreenIsTakenFromGnomesToolAndGivenBack()
    {
        fake.Values[$"{Shell}|show-screenshot-ui"] = "['Print']";
        using (var shortcuts = Shortcuts())
        {
            Assert.True(shortcuts.Register(HotKeyAction.Area, new HotKeyBinding(0x2C, [])));
            Assert.Equal("[]", fake.Values[$"{Shell}|show-screenshot-ui"]);
        }
        Assert.Equal("['Print']", fake.Values[$"{Shell}|show-screenshot-ui"]);
    }

    [Fact]
    public void PrintScreenLeftTakenByACopyThatWasKilledIsStillGivenBack()
    {
        fake.Values[$"{Shell}|show-screenshot-ui"] = "['Print', '<Super>Print']";
        Shortcuts().Register(HotKeyAction.Area, new HotKeyBinding(0x2C, []));
        // Killed: never disposed. The next copy finds Print gone, and knows it was there.
        using (var next = Shortcuts()) next.Register(HotKeyAction.Area, new HotKeyBinding(0x2C, []));
        Assert.Equal("['Print', '<Super>Print']", fake.Values[$"{Shell}|show-screenshot-ui"]);
    }

    [Fact]
    public void AShortcutLeftByACrashedCopyIsTakenAwayToo()
    {
        fake.Values[$"{MediaKeys}|custom-keybindings"] = $"['{Path}tinysnap-area/', '{Path}custom0/']";
        fake.Values[$"{Custom}:{Path}tinysnap-area/|binding"] = "'Print'";
        using (var shortcuts = Shortcuts()) shortcuts.UnregisterAll();
        Assert.Equal($"['{Path}custom0/']", fake.Values[$"{MediaKeys}|custom-keybindings"]);
        Assert.False(fake.Values.ContainsKey($"{Custom}:{Path}tinysnap-area/|binding"));
    }

    [Theory]
    [InlineData(0x2CU, new ModifierKey[0], "Print")]
    [InlineData(0x4FU, new[] { ModifierKey.Control, ModifierKey.Shift }, "<Shift><Control>o")]
    [InlineData(0x70U, new[] { ModifierKey.Windows }, "<Super>F1")]
    [InlineData(0xBDU, new[] { ModifierKey.Alt }, "<Alt>minus")]
    public void KeysAreNamedAsGnomeNamesThem(uint key, ModifierKey[] modifiers, string expected) =>
        Assert.Equal(expected, GnomeShortcuts.Accelerator(new HotKeyBinding(key, [.. modifiers])));
}
