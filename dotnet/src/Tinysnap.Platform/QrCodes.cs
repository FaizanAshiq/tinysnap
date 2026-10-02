using System.Runtime.InteropServices;
using SkiaSharp;
using Tinysnap.Core;
using ZXing;

namespace Tinysnap.Platform;

/// <summary>QR codes through ZXing.Net, on the device, the same on every system.</summary>
public static class QrCodes
{
    /// <summary>Every QR code, each payload once and in the order found: one code can be
    /// reported more than once.</summary>
    public static TextReading Read(SKImage image)
    {
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = { PossibleFormats = [BarcodeFormat.QR_CODE], TryHarder = true },
        };
        var found = reader.DecodeMultiple(Pixels(image), image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32) ?? [];
        var seen = new HashSet<string>();
        return new TextReading([.. found.Select(result => result.Text).Where(text => text is not null && seen.Add(text))], []);
    }

    /// <summary>Premultiplied blue, green, red and alpha rows, as the readers take them.</summary>
    public static byte[] Pixels(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            if (!image.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0))
                throw new InvalidOperationException("the image could not be read");
        }
        finally { handle.Free(); }
        return pixels;
    }
}
