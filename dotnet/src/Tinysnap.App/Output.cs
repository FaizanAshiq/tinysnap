using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.App;

/// <summary>A copy or save that could not be done, with a sentence fit for a dialog.</summary>
public sealed class OutputException(string message) : Exception(message);

/// <summary>Copy and save for everything that holds a finished image: the editor, pins and the
/// thumbnail, so each lands on the clipboard and on disk the same way.</summary>
internal static class Output
{
    /// <summary>The document flattened at its size, or the Export setting's when it has none, and
    /// its PNG with the DPI that pastes it at its size on screen.</summary>
    public static (ExportedImage Exported, byte[] Png)? Export(Document document, ExportScale scale)
    {
        if (Exporter.Export(document, scale) is not { } exported || Exporter.PngData(exported) is not { } png) return null;
        return (exported, png);
    }

    public static bool Copy(IClipboard clipboard, ExportedImage exported, byte[] png) =>
        clipboard.SetImage(exported.Image, png, exported.Dpi);

    /// <summary>Into <paramref name="folder"/>, made if it is missing, under a name for
    /// <paramref name="now"/>, numbered when that is taken. Written beside its name and then
    /// moved into it, so a half written file never has the name.</summary>
    public static string Save(byte[] png, string folder, DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new OutputException($"Tinysnap could not use the save folder {folder}. {error.Message}");
        }
        var path = Path.Combine(folder, FileNaming.FileName(now, name => File.Exists(Path.Combine(folder, name))));
        var partial = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(partial, png);
            File.Move(partial, path);
            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw new OutputException($"Tinysnap could not save to {folder}. {error.Message}");
        }
    }

    /// <summary>A PNG for dragging into another app, which takes a file more readily than image
    /// data, in the temporary folder's Tinysnap folder. Null when it cannot be written.</summary>
    public static string? TemporaryFile(byte[] png, DateTimeOffset now)
    {
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "Tinysnap");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileNaming.FileName(now, name => File.Exists(Path.Combine(folder, name))));
            File.WriteAllBytes(path, png);
            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
