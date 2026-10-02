using System.Globalization;

namespace Tinysnap.Linux;

/// <summary>Which of Tesseract's languages to read with: English, then the person's own from
/// <c>LANGUAGE</c> and <c>LANG</c>, each only where its data is there.</summary>
internal static class TesseractLanguages
{
    /// <summary>Tesseract's own names, where they are not the ISO three letter code.</summary>
    private static readonly Dictionary<string, string> Names = new()
    {
        ["en"] = "eng", ["fr"] = "fra", ["it"] = "ita", ["de"] = "deu", ["es"] = "spa", ["pt"] = "por",
        ["ko"] = "kor", ["ja"] = "jpn", ["ru"] = "rus", ["uk"] = "ukr", ["th"] = "tha", ["vi"] = "vie", ["ar"] = "ara",
    };

    /// <summary>Tesseract's language list, as <c>eng+fra</c>; empty when no data is there at all.</summary>
    public static string For(string? language, string? lang, IReadOnlySet<string> available)
    {
        var locales = (language ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries).Append(lang ?? "");
        var wanted = new[] { "eng" }.Concat(locales.Select(Name).OfType<string>()).Distinct().Where(available.Contains).ToList();
        return string.Join('+', wanted);
    }

    /// <summary>A locale such as <c>zh_TW.UTF-8</c> as Tesseract names its language.</summary>
    private static string? Name(string locale)
    {
        var bare = locale.Split('.', '@')[0];
        var parts = bare.Split('_');
        var code = parts[0].ToLowerInvariant();
        if (code is "" or "c" or "posix") return null;
        if (code == "zh") return parts.Length > 1 && parts[1].ToUpperInvariant() is "TW" or "HK" or "MO" ? "chi_tra" : "chi_sim";
        if (Names.TryGetValue(code, out var name)) return name;
        try
        {
            return CultureInfo.GetCultureInfo(code).ThreeLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The languages a folder holds data for.</summary>
    public static IReadOnlySet<string> Available(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.traineddata").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet()
            : [];

    /// <summary>One folder with every language, since Tesseract reads from one: the bundled
    /// folder as it is, or, when the system has languages it lacks, a folder of links to both,
    /// the bundled data winning where both have a language.</summary>
    public static string Folder(string bundled, string? system, string cache)
    {
        var extra = system is null ? [] : Available(system).Except(Available(bundled)).ToList();
        if (extra.Count == 0) return bundled;
        if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        Directory.CreateDirectory(cache);
        foreach (var name in extra) File.CreateSymbolicLink(Path.Combine(cache, $"{name}.traineddata"), Path.Combine(system!, $"{name}.traineddata"));
        foreach (var name in Available(bundled)) File.CreateSymbolicLink(Path.Combine(cache, $"{name}.traineddata"), Path.Combine(bundled, $"{name}.traineddata"));
        return cache;
    }
}
