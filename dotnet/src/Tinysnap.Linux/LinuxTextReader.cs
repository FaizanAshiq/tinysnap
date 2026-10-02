using System.Runtime.InteropServices;
using SkiaSharp;
using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.Linux;

/// <summary>Text through Tesseract, bundled with its languages, and QR codes through
/// <see cref="QrCodes"/>, both on the device. Screen text is small and smooth, so the capture is
/// read at twice its size, in grey, with a margin of its own edge colour so a letter touching
/// the edge is not dropped.</summary>
/// <param name="tessdata">The language folder; the bundled one, joined with the system's, when null.</param>
internal sealed class LinuxTextReader(string? tessdata = null) : ITextReader
{
    /// <summary>Longest side Tesseract is given, after scaling: 4K read at twice its size.</summary>
    private const int MaxSide = 8000;

    private const int Margin = 24;

    private static readonly Lazy<string> DefaultFolder = new(Folder);

    // One engine for the whole app, kept between reads: loading its languages takes longer than
    // reading a line. Tesseract is not safe for threads, so reads take turns.
    private static readonly Lock Gate = new();
    private static (nint Api, string Key)? engine;

    public Task<TextReading?> Read(SKImage image, bool codes) =>
        codes ? Task.FromResult<TextReading?>(QrCodes.Read(image)) : Task.Run(() => ReadText(image));

    private TextReading? ReadText(SKImage image)
    {
        var folder = tessdata ?? DefaultFolder.Value;
        var languages = TesseractLanguages.For(Environment.GetEnvironmentVariable("LANGUAGE"), Environment.GetEnvironmentVariable("LANG"),
                                               TesseractLanguages.Available(folder));
        if (languages.Length == 0) return null;
        var scale = Math.Min(2.0, (double)MaxSide / Math.Max(image.Width, image.Height));
        var (pixels, width, height) = Grey(image, scale);
        try
        {
            lock (Gate)
            {
                if (Engine(folder, languages) is not { } api) return null;
                return new TextReading([], Tinysnap.Core.TextReader.InReadingOrder(Lines(api, pixels, width, height, scale)));
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>The engine for these languages, made once; null when Tesseract cannot load them.</summary>
    private static nint? Engine(string folder, string languages)
    {
        var key = $"{folder}|{languages}";
        if (engine is { } kept && kept.Key == key) return kept.Api;
        if (engine is { } old) Tesseract.TessBaseAPIDelete(old.Api);
        engine = null;
        var api = Tesseract.TessBaseAPICreate();
        if (Tesseract.TessBaseAPIInit3(api, folder, languages) != 0)
        {
            Tesseract.TessBaseAPIDelete(api);
            return null;
        }
        engine = (api, key);
        return api;
    }

    private static unsafe List<(string Text, Rect Box)> Lines(nint api, byte[] pixels, int width, int height, double scale)
    {
        fixed (byte* data = pixels) Tesseract.TessBaseAPISetImage(api, data, width, height, 1, width);
        Tesseract.TessBaseAPISetSourceResolution(api, 200);
        var lines = new List<(string, Rect)>();
        try
        {
            if (Tesseract.TessBaseAPIRecognize(api, 0) != 0) return lines;
            var iterator = Tesseract.TessBaseAPIGetIterator(api);
            if (iterator == 0) return lines;
            try
            {
                var page = Tesseract.TessResultIteratorGetPageIterator(iterator);
                do
                {
                    var raw = Tesseract.TessResultIteratorGetUTF8Text(iterator, Tesseract.TextLine);
                    if (raw == 0) continue;
                    var text = Marshal.PtrToStringUTF8(raw)?.Trim() ?? "";
                    Tesseract.TessDeleteText(raw);
                    if (text.Length == 0 || Tesseract.TessPageIteratorBoundingBox(page, Tesseract.TextLine, out var left, out var top, out var right, out var bottom) == 0)
                        continue;
                    // Back from the scaled, margined picture to the capture's own pixels.
                    lines.Add((text, new Rect((left - Margin) / scale, (top - Margin) / scale, (right - left) / scale, (bottom - top) / scale)));
                }
                while (Tesseract.TessResultIteratorNext(iterator, Tesseract.TextLine) != 0);
            }
            finally { Tesseract.TessResultIteratorDelete(iterator); }
        }
        finally { Tesseract.TessBaseAPIClear(api); }
        return lines;
    }

    /// <summary>The capture scaled, on a margin of its top left pixel's colour, one grey byte a pixel.</summary>
    private static (byte[] Pixels, int Width, int Height) Grey(SKImage image, double scale)
    {
        int width = (int)Math.Round(image.Width * scale) + 2 * Margin, height = (int)Math.Round(image.Height * scale) + 2 * Margin;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var surface = SKSurface.Create(info);
        using (var corner = SKBitmap.FromImage(image)) surface.Canvas.Clear(corner.GetPixel(0, 0).WithAlpha(255));
        surface.Canvas.DrawImage(image, SKRect.Create(Margin, Margin, width - 2 * Margin, height - 2 * Margin),
                                 new SKSamplingOptions(SKCubicResampler.Mitchell));
        var rgba = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try { surface.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0); }
        finally { handle.Free(); }
        var grey = new byte[width * height];
        for (int i = 0, p = 0; i < grey.Length; i++, p += 4)
            grey[i] = (byte)((rgba[p] * 299 + rgba[p + 1] * 587 + rgba[p + 2] * 114) / 1000);
        return (grey, width, height);
    }

    private static string Folder()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tessdata");
        string[] places = ["/usr/share/tesseract-ocr/5/tessdata", "/usr/share/tesseract-ocr/4.00/tessdata", "/usr/share/tesseract/tessdata", "/usr/share/tessdata"];
        var system = places.FirstOrDefault(Directory.Exists);
        if (!Directory.Exists(bundled)) return system ?? bundled;
        var cache = Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "Tinysnap", "tessdata");
        return TesseractLanguages.Folder(bundled, system, cache);
    }
}
