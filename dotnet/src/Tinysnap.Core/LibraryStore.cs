using System.Globalization;
using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>One capture in the library: a folder named for its capture time.</summary>
public sealed record LibraryEntry(string Folder, DateTimeOffset Captured)
{
    public string Name => Path.GetFileName(Folder);

    /// <summary>The captured pixels, never changed.</summary>
    public string OriginalPath => Path.Combine(Folder, "original.png");

    /// <summary>Crop and annotations, rewritten as they change.</summary>
    public string EditsPath => Path.Combine(Folder, "edits.json");

    /// <summary>The rendered result, for preview, drag, copy and pin.</summary>
    public string ImagePath => Path.Combine(Folder, "image.png");
}

/// <summary>An entry read back: editable when its edits could be rebuilt exactly, otherwise a
/// plain capture of the best image left, so a damaged entry never loses the capture.</summary>
public sealed record OpenedEntry(Document Document, bool IsEditable);

/// <summary>The folders behind the library. Plain file operations, safe to call from any
/// thread; the app serialises writes to one entry by writing it from one place.</summary>
public sealed class LibraryStore(string? root = null)
{
    public const int KeepDays = 30;

    private const string NameFormat = "yyyy-MM-dd HH.mm.ss";

    public string Root { get; } = root ?? DefaultRoot;

    /// <summary>Beside the preferences, in the app data folder. Never in the local one: on
    /// Windows the app itself is installed there, and uninstalling deletes that whole folder.</summary>
    // ponytail: roaming app data, which a roaming profile would carry between machines; a local
    // folder outside the install if that ever matters.
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tinysnap", "Library");

    // Writing

    /// <summary>Creates the entry and writes all three files. The folder's creation time is
    /// set to the capture time, whole seconds, which is what listing and the sweep read, so
    /// even an entry whose edits are damaged keeps its place and its age.</summary>
    public LibraryEntry Add(Capture capture, DateTimeOffset captured, TimeZoneInfo? zone = null)
    {
        var whole = DateTimeOffset.FromUnixTimeSeconds(captured.ToUnixTimeSeconds());
        Directory.CreateDirectory(Root);
        var folder = Path.Combine(Root, FreeName(whole, zone ?? TimeZoneInfo.Local));
        Directory.CreateDirectory(folder);
        Directory.SetCreationTimeUtc(folder, whole.UtcDateTime);
        var entry = new LibraryEntry(folder, whole);

        var png = Png(capture.Image, capture.Scale);
        WriteAtomically(entry.OriginalPath, png);
        SaveEdits(new Document(capture), entry);
        WriteAtomically(entry.ImagePath, png);
        return entry;
    }

    /// <summary>Rewrites <c>edits.json</c>, writes any pasted image or backdrop wallpaper not
    /// yet on disk, and removes those nothing uses any more. Their pixels never change, so one
    /// already written is left alone.</summary>
    public void SaveEdits(Document document, LibraryEntry entry)
    {
        var (json, images) = DocumentArchive.Encode(document, entry.Captured);
        foreach (var (name, image) in images)
        {
            var path = Path.Combine(entry.Folder, name);
            if (!File.Exists(path)) WriteAtomically(path, Png(image, 1));
        }
        WriteAtomically(entry.EditsPath, json);
        // A file time moves only with the clock's tick, 15 ms on Windows, so edits written just
        // after the image can carry its time. Dated a moment past it, they read as newer.
        if (Modified(entry.ImagePath) is { } rendered && Modified(entry.EditsPath) <= rendered)
            File.SetLastWriteTimeUtc(entry.EditsPath, rendered.UtcDateTime.AddMilliseconds(1));
        foreach (var path in Directory.EnumerateFiles(entry.Folder))
        {
            var file = Path.GetFileName(path);
            if ((file.StartsWith("pasted-", StringComparison.Ordinal) || file.StartsWith("backdrop-", StringComparison.Ordinal))
                && !images.ContainsKey(file))
                TryQuietly(() => File.Delete(path));
        }
    }

    /// <summary>Renders the document at its size, or at full resolution when it has none, crop
    /// applied. <paramref name="editsAsOf"/> dates the image by the edits it was drawn from
    /// rather than by when it was written: a render off the UI thread can land after newer
    /// edits, and dated this way it still reads as stale and is drawn again.</summary>
    public void SaveImage(Document document, LibraryEntry entry, DateTimeOffset? editsAsOf = null)
    {
        var png = Exporter.Export(document, ExportScale.Native) is { } exported ? Exporter.PngData(exported) : null;
        WriteAtomically(entry.ImagePath, png ?? throw new IOException("the image could not be drawn"));
        if (editsAsOf is { } date) File.SetLastWriteTimeUtc(entry.ImagePath, date.UtcDateTime);
    }

    /// <summary>When the entry's edits were last written.</summary>
    public DateTimeOffset? EditsDate(LibraryEntry entry) => Modified(entry.EditsPath);

    // Reading

    /// <summary>Newest first. A folder with neither image is not an entry.</summary>
    public IReadOnlyList<LibraryEntry> Entries()
    {
        if (!Directory.Exists(Root)) return [];
        return new DirectoryInfo(Root).EnumerateDirectories()
            .Where(folder => !folder.Name.StartsWith('.') && !folder.Attributes.HasFlag(FileAttributes.Hidden))
            .Select(folder => Created(folder) is { } created ? new LibraryEntry(folder.FullName, created) : null)
            .OfType<LibraryEntry>()
            .Where(entry => File.Exists(entry.ImagePath) || File.Exists(entry.OriginalPath))
            .OrderByDescending(entry => entry.Captured)
            .ThenByDescending(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
    }

    public OpenedEntry? Open(LibraryEntry entry)
    {
        if (ReadImage(entry.OriginalPath) is { } original && ReadEdits(entry) is { } edits)
        {
            var document = new Document(new Capture(original.Image, edits.Scale), edits.Crop, edits.Annotations, edits.Backdrop);
            // Held to the limits the Size panel holds it to, whatever the file asks for.
            return new OpenedEntry(document with { Resize = edits.Resize is { } r ? document.ClampedResize(r) : null }, true);
        }
        if ((ReadImage(entry.ImagePath) ?? ReadImage(entry.OriginalPath)) is not { } flat) return null;
        return new OpenedEntry(new Document(new Capture(flat.Image, flat.Scale)), false);
    }

    private static ArchivedEdits? ReadEdits(LibraryEntry entry)
    {
        try
        {
            // Only a file in the entry's own folder: a name read from edits.json with a path in
            // it could reach a picture anywhere on disk.
            return DocumentArchive.Decode(File.ReadAllBytes(entry.EditsPath),
                                          name => IsPlainName(name) ? ReadImage(Path.Combine(entry.Folder, name))?.Image : null);
        }
        catch (Exception error) when (error is ArchiveException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsPlainName(string name) =>
        name.Length > 0 && name is not "." and not ".." && name.IndexOfAny(['/', '\\']) < 0 && Path.GetFileName(name) == name;

    /// <summary>True after a crash between an edit and the next render: the edits are newer
    /// than the image, so the image is rendered again before it is shown. Anything closer than
    /// 10 microseconds is the same moment, as on the Mac, whose file dates come back up to a
    /// microsecond early; no edit lands that soon after a render.</summary>
    public bool ImageIsStale(LibraryEntry entry)
    {
        if (Modified(entry.EditsPath) is not { } edits || Modified(entry.ImagePath) is not { } image) return false;
        return (edits - image).TotalSeconds > 0.000_01;
    }

    /// <summary>The bytes every entry takes, for Settings.</summary>
    public long Size()
    {
        if (!Directory.Exists(Root)) return 0;
        return new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
    }

    // Removing

    /// <summary>Deletes every entry more than 30 days old, except those named in
    /// <paramref name="keeping"/>, which are open in an editor. Returns what went.</summary>
    public IReadOnlyList<LibraryEntry> Sweep(DateTimeOffset? now = null, IReadOnlySet<string>? keeping = null)
    {
        var cutoff = (now ?? DateTimeOffset.UtcNow).AddDays(-KeepDays);
        var old = Entries().Where(entry => entry.Captured < cutoff && keeping?.Contains(entry.Name) != true).ToList();
        foreach (var entry in old) TryQuietly(() => Directory.Delete(entry.Folder, recursive: true));
        return old;
    }

    public void Clear(IReadOnlySet<string>? keeping = null)
    {
        foreach (var entry in Entries().Where(entry => keeping?.Contains(entry.Name) != true))
            TryQuietly(() => Directory.Delete(entry.Folder, recursive: true));
    }

    // Files

    /// <summary><c>2026-09-25 07.42.10</c>, or with <c> 2</c>, <c> 3</c> and so on when that is taken.</summary>
    private string FreeName(DateTimeOffset date, TimeZoneInfo zone)
    {
        var name = TimeZoneInfo.ConvertTime(date, zone).ToString(NameFormat, CultureInfo.InvariantCulture);
        var free = name;
        for (var number = 2; Directory.Exists(Path.Combine(Root, free)) || File.Exists(Path.Combine(Root, free)); number++)
            free = $"{name} {number}";
        return free;
    }

    /// <summary>The capture time. Windows and macOS keep a folder's creation time as set;
    /// Linux file systems cannot set one, so there the folder's name, written in local time,
    /// stands in.</summary>
    private static DateTimeOffset? Created(DirectoryInfo folder)
    {
        if (!OperatingSystem.IsLinux()) return new DateTimeOffset(folder.CreationTimeUtc, TimeSpan.Zero);
        // ponytail: untested until the Linux milestone, and a name taken in another time zone reads an offset out.
        var name = folder.Name.Length >= NameFormat.Length ? folder.Name[..NameFormat.Length] : folder.Name;
        return DateTime.TryParseExact(name, NameFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local)
            ? new DateTimeOffset(local)
            : null;
    }

    private static DateTimeOffset? Modified(string path) =>
        File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;

    /// <summary>Written beside the target, then moved over it, so a reader never sees half a file.</summary>
    private static void WriteAtomically(string path, byte[] bytes)
    {
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>For removals whose failure leaves nothing worse than a file that stays.</summary>
    private static void TryQuietly(Action remove)
    {
        try { remove(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A PNG that records its pixels per point as DPI, the way exports do, so the
    /// scale comes back with it.</summary>
    private static byte[] Png(SKImage image, double scale) =>
        Core.Png.Encode(image, 72 * scale) ?? throw new IOException("the PNG could not be written");

    /// <summary>An image and its pixels per point, read from the DPI it was written with.</summary>
    public static (SKImage Image, double Scale)? ReadImage(string path)
    {
        try
        {
            return File.Exists(path) ? Core.Png.Decode(File.ReadAllBytes(path)) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
