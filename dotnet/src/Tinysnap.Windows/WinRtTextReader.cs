using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using SkiaSharp;
using Tinysnap.Core;
using Tinysnap.Platform;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using ZXing;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.Windows;

/// <summary>Text through Windows' own recogniser, in the languages the person set up, and QR codes
/// through ZXing.Net, both on the device.</summary>
internal sealed class WinRtTextReader : ITextReader
{
    public async Task<TextReading?> Read(SKImage image, bool codes)
    {
        try
        {
            var pixels = Pixels(image);
            if (codes) return Codes(pixels, image.Width, image.Height);
            if (OcrEngine.TryCreateFromUserProfileLanguages() is not { } engine) return null;
            return await Text(engine, image);
        }
        catch (Exception error) when (error is COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Every QR code, each payload once and in the order found: one code can be
    /// reported more than once.</summary>
    private static TextReading Codes(byte[] pixels, int width, int height)
    {
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = { PossibleFormats = [BarcodeFormat.QR_CODE], TryHarder = true },
        };
        var found = reader.DecodeMultiple(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGRA32) ?? [];
        var seen = new HashSet<string>();
        return new TextReading([.. found.Select(result => result.Text).Where(text => text is not null && seen.Add(text))], []);
    }

    private static async Task<TextReading> Text(OcrEngine engine, SKImage image)
    {
        // The recogniser takes nothing past its limit on either side, so a very large capture is
        // read shrunk to fit, and its boxes need no mapping back, only their order.
        var limit = (int)OcrEngine.MaxImageDimension;
        var fit = Math.Min(1.0, (double)limit / Math.Max(image.Width, image.Height));
        using var sized = fit < 1 ? Shrunk(image, fit) : null;
        var source = sized ?? image;
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(Pixels(source).AsBuffer(), BitmapPixelFormat.Bgra8, source.Width, source.Height,
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

    /// <summary>Premultiplied blue, green, red and alpha rows, as both readers take them.</summary>
    private static byte[] Pixels(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new byte[info.BytesSize];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            if (!image.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0))
                throw new InvalidOperationException("the image could not be read");
        }
        finally { handle.Free(); }
        return pixels;
    }
}
