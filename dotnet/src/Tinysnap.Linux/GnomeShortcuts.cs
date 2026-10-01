using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.Linux;

/// <summary>Tinysnap's hotkeys as GNOME custom shortcuts: each runs the <c>gdbus</c> command that
/// reaches the running copy, so they work on Wayland, where an app cannot see keys pressed in
/// other apps. They are there while Tinysnap runs and removed when it quits, and a combination
/// GNOME or the person already uses is refused as taken. Print Screen is the exception: GNOME's
/// screenshot tool is moved off it while Tinysnap holds it and given it back as it was, even
/// after a crash.</summary>
internal sealed class GnomeShortcuts : IHotkeys
{
    private const string MediaKeys = "org.gnome.settings-daemon.plugins.media-keys";
    private const string CustomList = "custom-keybindings";
    private const string Custom = "org.gnome.settings-daemon.plugins.media-keys.custom-keybinding";
    private const string Root = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/";
    private const string Ours = Root + "tinysnap-";
    private const string Shell = "org.gnome.shell.keybindings";
    private const string ScreenshotUi = "show-screenshot-ui";

    private static readonly string[] GnomeSchemas =
    [
        "org.gnome.desktop.wm.keybindings", Shell, "org.gnome.mutter.keybindings", "org.gnome.mutter.wayland.keybindings", MediaKeys,
    ];

    private readonly GSettings settings;
    private readonly string busName;

    /// <summary>GNOME's own Print Screen bindings before Tinysnap took Print, kept on disk until
    /// they go back, so a copy killed while holding the key is put right by the next.</summary>
    private readonly string screenshotUiBefore;

    public GnomeShortcuts(GSettings settings, string busName = AppBus.Name, string? stateFolder = null)
    {
        this.settings = settings;
        this.busName = busName;
        var folder = stateFolder ?? Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"), "Tinysnap");
        screenshotUiBefore = Path.Combine(folder, "gnome-show-screenshot-ui");
    }

    public event Action<HotKeyAction>? Pressed;

    /// <summary>The app service received a shortcut's call.</summary>
    public void Press(HotKeyAction action) => Pressed?.Invoke(action);

    public bool Register(HotKeyAction action, HotKeyBinding binding)
    {
        if (Accelerator(binding) is not { } accelerator) return false;
        if (accelerator == "Print") TakePrintScreen();
        else if (UsedElsewhere(accelerator)) return false;
        var path = $"{Ours}{Json.Wire(action).ToLowerInvariant()}/";
        var schema = $"{Custom}:{path}";
        settings.Set(schema, "name", GSettings.Quote($"Tinysnap: {action.Title()}"));
        settings.Set(schema, "command", GSettings.Quote(AppBus.Command(busName, action)));
        settings.Set(schema, "binding", GSettings.Quote(accelerator));
        var list = settings.GetStrings(MediaKeys, CustomList);
        if (!list.Contains(path)) settings.SetStrings(MediaKeys, CustomList, [.. list, path]);
        return true;
    }

    /// <summary>Removes every shortcut Tinysnap added, a crashed copy's included, and gives Print
    /// Screen back; the person's own shortcuts stay as they were.</summary>
    public void UnregisterAll()
    {
        var list = settings.GetStrings(MediaKeys, CustomList);
        var ours = list.Where(path => path.StartsWith(Ours)).ToList();
        foreach (var path in ours) settings.ResetAll($"{Custom}:{path}");
        if (ours.Count > 0) settings.SetStrings(MediaKeys, CustomList, list.Except(ours));
        GivePrintScreenBack();
    }

    public void Dispose() => UnregisterAll();

    private void TakePrintScreen()
    {
        var current = settings.GetStrings(Shell, ScreenshotUi);
        if (!File.Exists(screenshotUiBefore))
        {
            if (!current.Contains("Print")) return;
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotUiBefore)!);
            File.WriteAllLines(screenshotUiBefore, current);
        }
        if (current.Contains("Print")) settings.SetStrings(Shell, ScreenshotUi, current.Where(key => key != "Print"));
    }

    private void GivePrintScreenBack()
    {
        if (!File.Exists(screenshotUiBefore)) return;
        settings.SetStrings(Shell, ScreenshotUi, File.ReadAllLines(screenshotUiBefore));
        File.Delete(screenshotUiBefore);
    }

    /// <summary>Whether GNOME, or one of the person's own shortcuts, already answers <paramref name="accelerator"/>.</summary>
    private bool UsedElsewhere(string accelerator)
    {
        var wanted = Normalized(accelerator);
        var theirs = settings.GetStrings(MediaKeys, CustomList).Where(path => !path.StartsWith(Ours))
            .Select(path => settings.Get($"{Custom}:{path}", "binding")).OfType<string>();
        return GnomeSchemas.SelectMany(settings.ListValues).Concat(theirs)
            .SelectMany(GSettings.ParseStrings)
            .Any(existing => Normalized(existing) == wanted);
    }

    /// <summary>An accelerator with its modifiers in one order and their aliases folded, so
    /// <c>&lt;Primary&gt;&lt;Shift&gt;1</c> and <c>&lt;Shift&gt;&lt;Control&gt;1</c> compare equal.</summary>
    private static string Normalized(string accelerator)
    {
        var modifiers = new SortedSet<string>(StringComparer.Ordinal);
        var rest = accelerator;
        while (rest.StartsWith('<') && rest.IndexOf('>') is > 0 and var end)
        {
            var name = rest[1..end].ToLowerInvariant();
            modifiers.Add(name is "primary" or "ctrl" or "control" ? "control" : name);
            rest = rest[(end + 1)..];
        }
        return string.Concat(modifiers.Select(m => $"<{m}>")) + rest.ToLowerInvariant();
    }

    /// <summary>The binding as GNOME writes it, or null for a key it has no name for here.</summary>
    public static string? Accelerator(HotKeyBinding binding)
    {
        string? key = binding.KeyCode switch
        {
            >= 0x30 and <= 0x39 => ((char)binding.KeyCode).ToString(),
            >= 0x41 and <= 0x5A => char.ToLowerInvariant((char)binding.KeyCode).ToString(),
            >= 0x60 and <= 0x69 => $"KP_{binding.KeyCode - 0x60}",
            >= 0x70 and <= 0x87 => $"F{binding.KeyCode - 0x6F}",
            0x20 => "space",
            0x21 => "Page_Up",
            0x22 => "Page_Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "Print",
            0x2D => "Insert",
            0xBA => "semicolon",
            0xBB => "equal",
            0xBC => "comma",
            0xBD => "minus",
            0xBE => "period",
            0xBF => "slash",
            0xC0 => "grave",
            0xDB => "bracketleft",
            0xDC => "backslash",
            0xDD => "bracketright",
            0xDE => "apostrophe",
            _ => null,
        };
        if (key is null) return null;
        (ModifierKey Key, string Name)[] order =
            [(ModifierKey.Shift, "<Shift>"), (ModifierKey.Control, "<Control>"), (ModifierKey.Alt, "<Alt>"), (ModifierKey.Windows, "<Super>")];
        return string.Concat(order.Where(m => binding.Modifiers.Contains(m.Key)).Select(m => m.Name)) + key;
    }
}
