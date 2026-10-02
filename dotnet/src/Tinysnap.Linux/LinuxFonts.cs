using System.Globalization;
using SkiaSharp;

namespace Tinysnap.Linux;

/// <summary>The font Tinysnap's text uses on Linux: GNOME's own interface font when it is
/// installed, else fontconfig's sans-serif, else a common sans face. Never left to fontconfig's
/// first ranked font, which in a C locale was C059, a serif face without the weights the windows
/// use, and the first editor crashed.</summary>
internal static class LinuxFonts
{
    private static readonly string[] Common =
        ["Cantarell", "Adwaita Sans", "Ubuntu Sans", "Ubuntu", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Open Sans", "Roboto", "Inter", "Arial"];

    /// <summary>Null when no font it knows of is installed, which leaves the choice to Avalonia.</summary>
    public static string? Choose(string? gnomeFont, string? sansSerif, IReadOnlySet<string> installed)
    {
        if (Family(gnomeFont, installed) is { } gnome) return gnome;
        if (!string.IsNullOrEmpty(sansSerif) && installed.Contains(sansSerif)) return sansSerif;
        return Common.FirstOrDefault(installed.Contains);
    }

    /// <summary>The family in a GNOME font name such as <c>'Noto Sans Medium 10'</c>: its size left
    /// off, then its last words one by one until an installed family remains.</summary>
    private static string? Family(string? gnomeFont, IReadOnlySet<string> installed)
    {
        var words = (gnomeFont ?? "").Trim().Trim('\'').Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && double.TryParse(words[^1], CultureInfo.InvariantCulture, out _)) words.RemoveAt(words.Count - 1);
        for (; words.Count > 0; words.RemoveAt(words.Count - 1))
            if (installed.Contains(string.Join(' ', words))) return string.Join(' ', words);
        return null;
    }

    /// <summary>For this desktop, from GNOME's setting and the fonts installed.</summary>
    public static string? ForThisDesktop(GSettings settings)
    {
        using var sans = SKFontManager.Default.MatchFamily("sans-serif");
        var installed = SKFontManager.Default.GetFontFamilies().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Choose(settings.Get("org.gnome.desktop.interface", "font-name"), sans?.FamilyName, installed);
    }
}
