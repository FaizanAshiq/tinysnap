using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>PNGs that record pixels per point as DPI, the way the Mac writes them, so a
/// capture's scale comes back with its pixels on either platform.</summary>
public static class Png
{
    public static byte[]? Encode(SKImage image, double dpi)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null) return null;
        var plain = data.ToArray();
        var perMetre = (uint)Geometry.Round(dpi / 0.0254);
        var physical = new byte[9];
        BinaryPrimitives.WriteUInt32BigEndian(physical.AsSpan(0), perMetre);
        BinaryPrimitives.WriteUInt32BigEndian(physical.AsSpan(4), perMetre);
        physical[8] = 1; // the unit is the metre
        // The signature is 8 bytes and IHDR always 25, so pHYs goes in at 33.
        return [.. plain.AsSpan(0, 33), .. Chunk("pHYs", physical), .. plain.AsSpan(33)];
    }

    public static (SKImage Image, double Scale)? Decode(byte[] data)
    {
        var image = SKImage.FromEncodedData(data);
        if (image is null) return null;
        // Decoded now rather than when first drawn, so a damaged file fails here and the raster
        // image can be read pixel by pixel.
        var raster = image.ToRasterImage(ensurePixelData: true);
        if (!ReferenceEquals(raster, image)) image.Dispose();
        return raster is null ? null : (raster, Scale(data));
    }

    /// <summary>72 DPI is one pixel per point. Kept to two decimals, not whole numbers, so a
    /// 150% display's 108 DPI reads 1.5.</summary>
    private static double Scale(byte[] data)
    {
        var at = 8;
        while (at + 12 <= data.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
            var type = Encoding.ASCII.GetString(data, at + 4, 4);
            if (type == "pHYs" && length == 9 && at + 17 <= data.Length && data[at + 16] == 1)
            {
                var dpi = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 8)) * 0.0254;
                return Math.Max(1, Math.Round(dpi / 72, 2, MidpointRounding.AwayFromZero));
            }
            if (type == "IDAT" || length < 0) break;
            at += 12 + length;
        }
        return 1;
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type, chunk.AsSpan(4));
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc(chunk.AsSpan(4, 4 + data.Length)));
        return chunk;
    }

    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in bytes) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
