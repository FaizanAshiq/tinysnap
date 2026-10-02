using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using SkiaSharp;
using Tinysnap.Core;
using Tinysnap.Platform;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.Windows;

/// <summary>Text through Windows' own recogniser, in the languages the person set up, and QR codes
/// through <see cref="QrCodes"/>, both on the device.</summary>
internal sealed class WinRtTextReader : ITextReader
{
    public async Task<TextReading?> Read(SKImage image, bool codes)
    {
        try
        {
            if (codes) return QrCodes.Read(image);
            if (OcrEngine.TryCreateFromUserProfileLanguages() is not { } engine) return null;
            return await Text(engine, image);
        }
        catch (Exception error) when (error is COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<TextReading> Text(OcrEngine engine, SKImage image)
    {
        // The recogniser takes nothing past its limit on either side, so a very large capture is
        // read shrunk to fit, and its boxes need no mapping back, only their order.
        var limit = (int)OcrEngine.MaxImageDimension;
        var fit = Math.Min(1.0, (double)limit / Math.Max(image.Width, image.Height));
        using var sized = fit < 1 ? Shrunk(image, fit) : null;
        var source = sized ?? image;
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(QrCodes.Pixels(source).AsBuffer(), BitmapPixelFormat.Bgra8, source.Width, source.Height,
                                                               BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        var lines = result.Lines
            .Where(line => line.Words.Count > 0)
            .Select(line => (line.Text, Box: Union(line.Words.Select(word => word.BoundingRect))))
            .ToList();
        return new TextReading([], Tinysnap.Core.TextReader.InReadingOrder(lines));
    }

    private static Rect Union(IEnumerable<global::Windows.Foundation.Rect> boxes)
    {
        var all = boxes.ToList();
        var left = all.Min(box => box.X);
        var top = all.Min(box => box.Y);
        return new Rect(left, top, all.Max(box => box.X + box.Width) - left, all.Max(box => box.Y + box.Height) - top);
    }

    private static SKImage Shrunk(SKImage image, double fit)
    {
        var info = new SKImageInfo(Math.Max(1, (int)(image.Width * fit)), Math.Max(1, (int)(image.Height * fit)));
        using var surface = SKSurface.Create(info);
        surface.Canvas.DrawImage(image, new SKRect(0, 0, info.Width, info.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return surface.Snapshot();
    }
}
