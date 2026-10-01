using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace Tinysnap.Core;

/// <summary>Written <c>control</c>, <c>shift</c>, <c>alt</c> and <c>windows</c>.</summary>
public enum ModifierKey { Control, Shift, Alt, Windows }

/// <summary>A Windows virtual key code and the modifiers held with it. Two bindings are the
/// same whatever order their modifiers are listed in, because a hand edited preferences file
/// can list them any way round.</summary>
public sealed record HotKeyBinding(uint KeyCode, ImmutableArray<ModifierKey> Modifiers)
{
    /// <summary>The order Windows prints modifiers in.</summary>
    private static readonly (ModifierKey Key, string Name)[] SystemOrder =
        [(ModifierKey.Control, "Ctrl"), (ModifierKey.Alt, "Alt"), (ModifierKey.Shift, "Shift"), (ModifierKey.Windows, "Win")];

    private readonly ImmutableArray<ModifierKey> modifiers = Modifiers.IsDefault ? [] : Modifiers;

    /// <summary>Never a default array, however the binding was made.</summary>
    public ImmutableArray<ModifierKey> Modifiers
    {
        get => modifiers;
        init => modifiers = value.IsDefault ? [] : value;
    }

    public bool Equals(HotKeyBinding? other) =>
        other is not null && KeyCode == other.KeyCode && Mask == other.Mask;

    public override int GetHashCode() => HashCode.Combine(KeyCode, Mask);

    private int Mask => Modifiers.Aggregate(0, (mask, key) => mask | 1 << (int)key);

    /// <summary>The binding written the way Windows prints it in a menu, for example
    /// <c>Ctrl+Shift+2</c>, with the modifiers in the system order however they were stored.</summary>
    public string DisplayString =>
        string.Join("+", SystemOrder.Where(m => (Mask & 1 << (int)m.Key) != 0).Select(m => m.Name).Append(Label(KeyCode)));

    /// <summary>Only the keys worth binding a hotkey to have names; anything else still reads
    /// as <c>Key 250</c> rather than as nothing at all.</summary>
    private static string Label(uint keyCode) => keyCode switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)keyCode).ToString(),
        >= 0x70 and <= 0x87 => $"F{keyCode - 0x6F}",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2C => "Print Screen",
        0x2D => "Insert",
        0x2E => "Delete",
        _ => $"Key {keyCode}",
    };

    public JsonObject ToJson() => new()
    {
        ["keyCode"] = KeyCode,
        ["modifiers"] = new JsonArray([.. Modifiers.Select(m => (JsonNode)Json.Wire(m))]),
    };

    /// <summary>Null for anything unreadable, which the caller reads as the default.</summary>
    public static HotKeyBinding? FromJson(JsonNode? node)
    {
        if (node is not JsonObject o || Json.Number(o, "keyCode") is not { } code
            || code != Math.Floor(code) || code < 0 || code > uint.MaxValue || Json.Array(o, "modifiers") is not { } list)
            return null;
        var modifiers = list.Select(m => m is JsonValue v && v.TryGetValue<string>(out var name) ? Json.FromWire<ModifierKey>(name) : null)
            .ToArray();
        return modifiers.All(m => m is not null) ? new HotKeyBinding((uint)code, [.. modifiers.Select(m => m!.Value)]) : null;
    }
}

/// <summary>In the order the tray menu and Settings list them.</summary>
public enum HotKeyAction { Area, Window, Fullscreen, Text, Qr, RepeatArea, Delayed, Library }

public static class HotKeyActions
{
    public static string Title(this HotKeyAction action) => action switch
    {
        HotKeyAction.Area => "Capture Area",
        HotKeyAction.Window => "Capture Window",
        HotKeyAction.Fullscreen => "Capture Fullscreen",
        HotKeyAction.Text => "Capture Text",
        HotKeyAction.Qr => "Scan QR Code",
        HotKeyAction.RepeatArea => "Repeat Last Area",
        HotKeyAction.Delayed => "Delayed Capture",
        _ => "Open Library",
    };
}

public sealed record HotKeys(HotKeyBinding? Area, HotKeyBinding? Window, HotKeyBinding? Fullscreen, HotKeyBinding? Text,
                             HotKeyBinding? Qr, HotKeyBinding? RepeatArea, HotKeyBinding? Delayed, HotKeyBinding? Library)
{
    /// <summary>Print Screen for an area, the key Windows and Linux keyboards have for it, and
    /// Ctrl+Shift with 1 and O, O for OCR. Window capture is in the menu until given a key.</summary>
    public static readonly HotKeys Defaults = new(
        new HotKeyBinding(0x2C, []),
        null,
        new HotKeyBinding(0x31, [ModifierKey.Control, ModifierKey.Shift]),
        new HotKeyBinding(0x4F, [ModifierKey.Control, ModifierKey.Shift]),
        null, null, null, null);

    public HotKeyBinding? this[HotKeyAction action] => action switch
    {
        HotKeyAction.Area => Area,
        HotKeyAction.Window => Window,
        HotKeyAction.Fullscreen => Fullscreen,
        HotKeyAction.Text => Text,
        HotKeyAction.Qr => Qr,
        HotKeyAction.RepeatArea => RepeatArea,
        HotKeyAction.Delayed => Delayed,
        _ => Library,
    };

    public HotKeys With(HotKeyAction action, HotKeyBinding? binding) => action switch
    {
        HotKeyAction.Area => this with { Area = binding },
        HotKeyAction.Window => this with { Window = binding },
        HotKeyAction.Fullscreen => this with { Fullscreen = binding },
        HotKeyAction.Text => this with { Text = binding },
        HotKeyAction.Qr => this with { Qr = binding },
        HotKeyAction.RepeatArea => this with { RepeatArea = binding },
        HotKeyAction.Delayed => this with { Delayed = binding },
        _ => this with { Library = binding },
    };

    /// <summary>The action already using <paramref name="binding"/>, so Settings can refuse a
    /// second one.</summary>
    public HotKeyAction? ActionUsing(HotKeyBinding binding) =>
        Enum.GetValues<HotKeyAction>().Cast<HotKeyAction?>().FirstOrDefault(action => this[action!.Value] == binding);

    /// <summary>Null is written as null, not left out. Left out, a hotkey the user cleared
    /// would read back as the default on the next launch.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject();
        foreach (var action in Enum.GetValues<HotKeyAction>()) o[Json.Wire(action)] = this[action]?.ToJson();
        return o;
    }

    /// <summary>A key that is there but null means no hotkey. A key that is missing, or holds
    /// something unreadable, means the default.</summary>
    public static HotKeys FromJson(JsonNode? node)
    {
        if (node is not JsonObject o) return Defaults;
        var hotkeys = Defaults;
        foreach (var action in Enum.GetValues<HotKeyAction>())
        {
            var key = Json.Wire(action);
            if (!o.ContainsKey(key)) continue;
            if (Json.IsNull(o, key)) hotkeys = hotkeys.With(action, null);
            else if (HotKeyBinding.FromJson(o[key]) is { } binding) hotkeys = hotkeys.With(action, binding);
        }
        return hotkeys;
    }
}

/// <summary>What a capture turns into once it is taken.</summary>
public enum AfterCapture { Editor, Thumbnail }

public sealed record Preferences
{
    public const int DelayMin = 1;
    public const int DelayMax = 60;

    public static readonly Preferences Defaults = new();

    private readonly Backdrop backdrop = Backdrop.Defaults;

    public HotKeys HotKeys { get; init; } = HotKeys.Defaults;

    /// <summary>Where Windows keeps screenshots, so Tinysnap's land beside them.</summary>
    public string SaveFolder { get; init; } = "~/Pictures/Screenshots";

    public ExportScale ExportScale { get; init; } = ExportScale.Native;
    public int DelaySeconds { get; init; } = 3;

    /// <summary>Hidden, Tinysnap is reached through its hotkeys, and opening the app again
    /// shows Settings.</summary>
    public bool ShowTrayIcon { get; init; } = true;

    /// <summary>The one colour every tool draws with, the last one picked.</summary>
    public string ColorHex { get; init; } = Palette.Red;

    /// <summary>The last style used per tool, keyed by the tool's wire name. Only the size and
    /// the box shape count; the colour is <c>ColorHex</c>.</summary>
    public IReadOnlyDictionary<string, Style> ToolStyles { get; init; } = ImmutableDictionary<string, Style>.Empty;

    public AfterCapture AfterCapture { get; init; } = AfterCapture.Editor;

    /// <summary>Every capture kept in the library for 30 days, annotations and all.</summary>
    public bool KeepLibrary { get; init; } = true;

    /// <summary>The backdrop last used, brought back when a capture's backdrop is turned on.
    /// The settings only: a wallpaper is read again for each capture, from its own screen.</summary>
    public Backdrop Backdrop
    {
        get => backdrop;
        init => backdrop = value with { Wallpaper = null };
    }

    /// <summary>The Measure tool as it was left: its lines, its edge contrast, its guide seen.</summary>
    public MeasureSettings Measure { get; init; } = MeasureSettings.Defaults;

    /// <summary>Tool styles compared by content, as the Mac's dictionary is.</summary>
    public bool Equals(Preferences? other) =>
        other is not null && HotKeys == other.HotKeys && SaveFolder == other.SaveFolder && ExportScale == other.ExportScale
        && DelaySeconds == other.DelaySeconds && ShowTrayIcon == other.ShowTrayIcon && ColorHex == other.ColorHex
        && ToolStyles.Count == other.ToolStyles.Count
        && ToolStyles.All(pair => other.ToolStyles.TryGetValue(pair.Key, out var style) && style == pair.Value)
        && AfterCapture == other.AfterCapture && KeepLibrary == other.KeepLibrary && Backdrop == other.Backdrop
        && Measure == other.Measure;

    public override int GetHashCode() =>
        HashCode.Combine(HotKeys, SaveFolder, ExportScale, DelaySeconds, ColorHex, ToolStyles.Count, Backdrop, Measure);

    /// <summary>The save folder with <c>~</c> as the user's profile folder and the platform's
    /// own separators.</summary>
    public string SaveFolderPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var folder = SaveFolder == "~" ? home
                : SaveFolder.StartsWith("~/", StringComparison.Ordinal) || SaveFolder.StartsWith("~\\", StringComparison.Ordinal)
                    ? Path.Join(home, SaveFolder[2..])
                    : SaveFolder;
            return Path.GetFullPath(folder);
        }
    }

    /// <summary>The remembered styles, for starting an editor session.</summary>
    public IReadOnlyDictionary<Tool, Style> Styles =>
        ToolStyles
            .Select(pair => (Tool: Json.FromWire<Tool>(pair.Key), Style: pair.Value))
            .Where(pair => pair.Tool is not null)
            .ToDictionary(pair => pair.Tool!.Value, pair => pair.Style);

    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tinysnap", "preferences.json");

    /// <summary>Returns defaults when the file does not exist, so first launch needs no setup.</summary>
    public static Preferences Load(string path)
    {
        if (!File.Exists(path)) return Defaults;
        return FromJson(Json.Parse(File.ReadAllBytes(path)) as JsonObject
                        ?? throw new InvalidDataException("preferences.json is not a JSON object"));
    }

    /// <summary>Written to a file beside it first, then moved over it, so a crash mid write
    /// never leaves half a file.</summary>
    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = $"{full}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporary, Json.WriteBytes(ToJson()));
        File.Move(temporary, full, overwrite: true);
    }

    public JsonObject ToJson() => new()
    {
        ["hotkeys"] = HotKeys.ToJson(),
        ["saveFolder"] = SaveFolder,
        ["exportScale"] = ExportScale == ExportScale.OneX ? "1x" : "native",
        ["delaySeconds"] = DelaySeconds,
        ["showTrayIcon"] = ShowTrayIcon,
        ["colorHex"] = ColorHex,
        ["toolStyles"] = new JsonObject(ToolStyles.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value.ToJson()))),
        ["afterCapture"] = Json.Wire(AfterCapture),
        ["keepLibrary"] = KeepLibrary,
        ["backdrop"] = Backdrop.ToJson(),
        ["measure"] = Measure.ToJson(),
    };

    /// <summary>Every key is optional and a bad value falls back on its own, so a file written
    /// by an older version, or a hand edited one, still loads.</summary>
    public static Preferences FromJson(JsonObject o)
    {
        var fallback = Defaults;
        var hex = Json.String(o, "colorHex");
        var styles = Json.Object(o, "toolStyles");
        return new Preferences
        {
            HotKeys = o.ContainsKey("hotkeys") ? HotKeys.FromJson(o["hotkeys"]) : fallback.HotKeys,
            SaveFolder = Json.String(o, "saveFolder") ?? fallback.SaveFolder,
            ExportScale = Json.String(o, "exportScale") switch
            {
                "native" => ExportScale.Native,
                "1x" => ExportScale.OneX,
                _ => fallback.ExportScale,
            },
            DelaySeconds = Math.Clamp(Json.Integer(o, "delaySeconds") ?? fallback.DelaySeconds, DelayMin, DelayMax),
            ShowTrayIcon = Json.Bool(o, "showTrayIcon") ?? fallback.ShowTrayIcon,
            ColorHex = hex is not null && Palette.Components(hex) is not null ? hex : fallback.ColorHex,
            // One style that is not an object makes the whole set unreadable, as on the Mac.
            ToolStyles = styles is not null && styles.All(pair => pair.Value is JsonObject)
                ? styles.ToImmutableDictionary(pair => pair.Key, pair => Style.FromJson(pair.Value))
                : fallback.ToolStyles,
            AfterCapture = Json.Enum<AfterCapture>(o, "afterCapture") ?? fallback.AfterCapture,
            KeepLibrary = Json.Bool(o, "keepLibrary") ?? fallback.KeepLibrary,
            Backdrop = Json.Object(o, "backdrop") is { } backdrop ? Backdrop.FromJson(backdrop) : fallback.Backdrop,
            Measure = Json.Object(o, "measure") is { } measure ? MeasureSettings.FromJson(measure) : fallback.Measure,
        };
    }
}
