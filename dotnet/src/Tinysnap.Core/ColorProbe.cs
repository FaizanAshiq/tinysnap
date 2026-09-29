using System.Runtime.InteropServices;
using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>The colour under the pointer, for the editor's readout that Tab copies.</summary>
public static class ColorProbe
{
    /// <summary>"#RRGGBB" of one pixel, counted from the top left corner, or null outside
    /// the image. Only that pixel is read, so this is cheap enough to run on every pointer move.</summary>
    public static string? Hex(SKImage image, int x, int y)
    {
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) return null;
        var info = new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        var pixel = new byte[4];
        var handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);
        try
        {
            if (!image.ReadPixels(info, handle.AddrOfPinnedObject(), 4, x, y)) return null;
        }
        finally { handle.Free(); }
        int alpha = pixel[3];
        if (alpha <= 0) return null;
        int Channel(byte value) => Math.Min(255, value * 255 / alpha);
        return $"#{Channel(pixel[0]):X2}{Channel(pixel[1]):X2}{Channel(pixel[2]):X2}";
    }
}
