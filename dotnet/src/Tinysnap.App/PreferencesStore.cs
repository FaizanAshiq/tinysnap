using System.Collections.Immutable;
using Tinysnap.Core;

namespace Tinysnap.App;

/// <summary>The preferences as the app last read or wrote them. Every change is merged into
/// the file as it is on disk, so a hand edit made while the app runs is not written over, and
/// is announced so the tray, the hotkeys and Settings follow it.</summary>
internal sealed class PreferencesStore(string path)
{
    public Preferences Current { get; private set; } = Read(path, Preferences.Defaults);

    /// <summary>The file the preferences live in.</summary>
    public string Path => path;

    public event Action<Preferences>? Changed;

    public void Update(Func<Preferences, Preferences> change)
    {
        var updated = change(Read(path, Current));
        try
        {
            updated.Save(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Kept for this run; the next change tries the file again.
        }
        Current = updated;
        Changed?.Invoke(updated);
    }

    /// <summary>Written when a change is finished, not on every tick of the colour spectrum,
    /// each of which would rewrite the file.</summary>
    public void RememberStyles(IReadOnlyDictionary<Tool, Style> styles, string colorHex) => Update(p => p with
    {
        ToolStyles = styles.Aggregate(p.ToolStyles.ToImmutableDictionary(),
                                      (all, pair) => all.SetItem(Json.Wire(pair.Key), pair.Value)),
        ColorHex = colorHex,
        RecentColors = Palette.Recent(colorHex, p.RecentColors),
    });

    /// <summary><paramref name="fallback"/> when the file cannot be read, and the defaults when
    /// it is damaged, rather than stopping a capture.</summary>
    private static Preferences Read(string path, Preferences fallback)
    {
        try
        {
            return Preferences.Load(path);
        }
        catch (InvalidDataException)
        {
            return Preferences.Defaults;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }
}
