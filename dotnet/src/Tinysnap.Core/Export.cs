using System.Globalization;
using SkiaSharp;

namespace Tinysnap.Core;

public enum ExportScale
{
    Native,
    /// <summary>Written <c>"1x"</c> in preferences.json, as the Mac writes it.</summary>
    OneX,
}

/// <summary>A finished image with its <paramref name="Dpi"/>, 72 times the pixels per point
/// and never under 72, so the PNG pastes at the size it had on screen, which is
/// <paramref name="PointSize"/>.</summary>
public sealed record ExportedImage(SKImage Image, double Dpi, Size PointSize);

public static class Exporter
{
    /// <summary>Output pixels per capture pixel: the capture's own size, or the Export
    /// setting's when it has none. Native keeps every capture pixel, 1x draws one pixel per point.</summary>
    public static double OutputScale(Document document, ExportScale setting) =>
        document.Resize ?? (setting == ExportScale.Native ? 1 : 1 / document.Scale);

    /// <summary>The output flattened at its size, inside its backdrop when there is one.</summary>
    public static ExportedImage? Export(Document document, ExportScale scale)
    {
        var outputScale = OutputScale(document, scale);
        var image = Renderer.RenderFramed(document, outputScale);
        if (image is null) return null;
        // Never under 72, so a capture of scale 2 keeps its size in points down to 50%, with
        // fewer pixels, and only shows smaller below that.
        var dpi = Math.Max(72, 72 * document.Scale * outputScale);
        return new ExportedImage(image, dpi, new Size(image.Width * 72 / dpi, image.Height * 72 / dpi));
    }

    /// <summary>What text and codes are read from: every capture pixel, whatever size the
    /// capture exports at, with the annotations drawn, so nothing under a blur or an erase is
    /// read back out, and the crop applied, but no backdrop, which holds nothing to read.
    /// <paramref name="area"/>, in capture pixels, narrows it to what was dragged over; null
    /// when that misses the output.</summary>
    public static SKImage? ReadingImage(Document document, Rect? area = null)
    {
        var output = Renderer.RenderOutput(document);
        if (output is null || area is not { } box) return output;
        var origin = document.OutputPixelRect.Origin;
        var cut = box.Offset(-origin.X, -origin.Y).Integral.Intersection(new Rect(0, 0, output.Width, output.Height));
        using (output)
        {
            if (cut.IsNull || cut.Width < 1 || cut.Height < 1) return null;
            return output.Subset(cut.ToSKRectI());
        }
    }

    public static byte[]? PngData(ExportedImage exported) => Png.Encode(exported.Image, exported.Dpi);
}

public static class FileNaming
{
    /// <summary>"Tinysnap 2026-09-25 at 09.41.12.png", the macOS screenshot pattern, or with
    /// " 2", " 3" and so on before the extension when that name is taken.</summary>
    public static string FileName(DateTimeOffset date, TimeZoneInfo zone, Func<string, bool> isTaken)
    {
        var local = TimeZoneInfo.ConvertTime(date, zone);
        var stem = "Tinysnap " + local.ToString("yyyy-MM-dd 'at' HH.mm.ss", CultureInfo.InvariantCulture);
        var candidate = stem + ".png";
        for (var number = 2; isTaken(candidate); number++) candidate = $"{stem} {number}.png";
        return candidate;
    }

    public static string FileName(DateTimeOffset date, Func<string, bool> isTaken) =>
        FileName(date, TimeZoneInfo.Local, isTaken);
}
