using System.Runtime.InteropServices;
using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>Raw sRGB bytes, premultiplied RGBA, row 0 at the top. Used where the renderer
/// has to work pixel by pixel: erase fills and redaction noise. The layout is fixed rather
/// than Skia's platform default, which is BGRA on Windows and would swap channels.</summary>
internal sealed class PixelBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Bytes { get; }

    private PixelBuffer(int width, int height, byte[] bytes)
    {
        Width = width;
        Height = height;
        Bytes = bytes;
    }

    private static SKImageInfo Info(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());

    public static PixelBuffer? From(SKImage image)
    {
        var info = Info(image.Width, image.Height);
        var bytes = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return image.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0)
                ? new PixelBuffer(image.Width, image.Height, bytes)
                : null;
        }
        finally { handle.Free(); }
    }

    public SKImage? MakeImage()
    {
        var info = Info(Width, Height);
        var handle = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), info.RowBytes);
            return SKImage.FromPixelCopy(pixmap);
        }
        finally { handle.Free(); }
    }

    /// <summary>How bright the pixels are on average, 0 to 1, each counted by how opaque it is, so
    /// a window's see-through corners do not read as black. Null when every pixel is see-through.</summary>
    public double? MeanLuminance
    {
        get
        {
            double sum = 0, weight = 0;
            for (var index = 0; index < Bytes.Length; index += 4)
            {
                // Premultiplied, so each pixel's brightness already comes scaled by its alpha.
                sum += 0.2126 * Bytes[index] + 0.7152 * Bytes[index + 1] + 0.0722 * Bytes[index + 2];
                weight += Bytes[index + 3];
            }
            return weight > 0 ? sum / weight : null;
        }
    }

    public (byte R, byte G, byte B, byte A) Pixel(int x, int y)
    {
        var index = (y * Width + x) * 4;
        return (Bytes[index], Bytes[index + 1], Bytes[index + 2], Bytes[index + 3]);
    }

    /// <summary>Fills the <paramref name="inner"/> box by blending the pixels just outside
    /// each of its edges, weighted by how near each edge is. A flat background comes back
    /// exactly, a gradient comes back as a gradient. An edge that has nothing outside it,
    /// because the box touches the border of the buffer, is left out of the blend.</summary>
    public void EraseFill((int X, int Y, int Width, int Height) inner)
    {
        var left = inner.X - 1;
        var right = inner.X + inner.Width;
        var top = inner.Y - 1;
        var bottom = inner.Y + inner.Height;
        var hasLeft = left >= 0;
        var hasRight = right < Width;
        var hasTop = top >= 0;
        var hasBottom = bottom < Height;
        if (!hasLeft && !hasRight && !hasTop && !hasBottom)
        {
            FillWithAverage(inner);
            return;
        }
        var rowBytes = Width * 4;
        // Each pixel just outside the box stands for its stretch of that edge: the median of
        // those within 16 pixels of it along the edge. A letter crossing the edge is a short
        // run of odd pixels and drops out, where on its own it streaked across the box row by
        // row. A gradient along the edge survives, since the median of an even slope is its middle.
        var leftEdge = hasLeft ? SmoothedEdge(inner.Height, i => (inner.Y + i) * rowBytes + left * 4) : [];
        var rightEdge = hasRight ? SmoothedEdge(inner.Height, i => (inner.Y + i) * rowBytes + right * 4) : [];
        var topEdge = hasTop ? SmoothedEdge(inner.Width, i => top * rowBytes + (inner.X + i) * 4) : [];
        var bottomEdge = hasBottom ? SmoothedEdge(inner.Width, i => bottom * rowBytes + (inner.X + i) * 4) : [];
        for (var y = inner.Y; y < inner.Y + inner.Height; y++)
        {
            for (var x = inner.X; x < inner.X + inner.Width; x++)
            {
                double r = 0, g = 0, b = 0, a = 0, weights = 0;
                void Add((byte R, byte G, byte B, byte A) pixel, int distance)
                {
                    var weight = 1.0 / distance;
                    r += pixel.R * weight;
                    g += pixel.G * weight;
                    b += pixel.B * weight;
                    a += pixel.A * weight;
                    weights += weight;
                }
                if (hasLeft) Add(leftEdge[y - inner.Y], x - left);
                if (hasRight) Add(rightEdge[y - inner.Y], right - x);
                if (hasTop) Add(topEdge[x - inner.X], y - top);
                if (hasBottom) Add(bottomEdge[x - inner.X], bottom - y);
                var index = y * rowBytes + x * 4;
                Bytes[index] = (byte)Geometry.Round(r / weights);
                Bytes[index + 1] = (byte)Geometry.Round(g / weights);
                Bytes[index + 2] = (byte)Geometry.Round(b / weights);
                Bytes[index + 3] = (byte)Geometry.Round(a / weights);
            }
        }
    }

    /// <summary>The edge pixels at <c>index(0..count)</c>, each the median, channel by
    /// channel, of those within 16 of it along the edge.</summary>
    private (byte R, byte G, byte B, byte A)[] SmoothedEdge(int count, Func<int, int> index)
    {
        var channels = Enumerable.Range(0, 4)
            .Select(channel => SlidingMedians([.. Enumerable.Range(0, count).Select(i => Bytes[index(i) + channel])], 16))
            .ToArray();
        return [.. Enumerable.Range(0, count).Select(i => (channels[0][i], channels[1][i], channels[2][i], channels[3][i]))];
    }

    /// <summary>For each value, the median of those within <paramref name="reach"/> of it, its window
    /// sorted as one: the window slides along, one value in and one out at each step. Sorting a fresh
    /// copy at every pixel made the medians most of an erase's cost.</summary>
    internal static byte[] SlidingMedians(byte[] values, int reach)
    {
        var window = new List<byte>(2 * reach + 1);
        // Where a value goes in the sorted window: before the first larger one.
        int Slot(byte value)
        {
            int low = 0, high = window.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (window[middle] <= value) low = middle + 1;
                else high = middle;
            }
            return low;
        }
        var medians = new byte[values.Length];
        int first = 0, last = -1;
        for (var i = 0; i < values.Length; i++)
        {
            while (last < Math.Min(values.Length - 1, i + reach))
            {
                last++;
                window.Insert(Slot(values[last]), values[last]);
            }
            while (first < i - reach)
            {
                // The slot after the last copy of the value, so one before it is a copy.
                window.RemoveAt(Slot(values[first]) - 1);
                first++;
            }
            medians[i] = window[window.Count / 2];
        }
        return medians;
    }

    /// <summary>Only when the box covers the whole buffer: there is nothing outside to blend.</summary>
    private void FillWithAverage((int X, int Y, int Width, int Height) inner)
    {
        var total = new long[4];
        var count = Math.Max(1, inner.Width * inner.Height);
        for (var index = 0; index < Bytes.Length; index += 4)
            for (var channel = 0; channel < 4; channel++) total[channel] += Bytes[index + channel];
        var average = total.Select(sum => (byte)(sum / count)).ToArray();
        for (var index = 0; index < Bytes.Length; index += 4)
            for (var channel = 0; channel < 4; channel++) Bytes[index + channel] = average[channel];
    }

    /// <summary>Replaces each <paramref name="block"/> square, counted from the top left
    /// corner, with its average. Averaged by hand: downscaling samples rather than averages,
    /// which left one pixel wide detail standing inside the blocks.</summary>
    public void Pixelate(int block)
    {
        block = Math.Max(1, block);
        for (var top = 0; top < Height; top += block)
        {
            for (var left = 0; left < Width; left += block)
            {
                var bottom = Math.Min(top + block, Height);
                var right = Math.Min(left + block, Width);
                int r = 0, g = 0, b = 0, a = 0;
                for (var y = top; y < bottom; y++)
                {
                    for (var x = left; x < right; x++)
                    {
                        var index = (y * Width + x) * 4;
                        r += Bytes[index];
                        g += Bytes[index + 1];
                        b += Bytes[index + 2];
                        a += Bytes[index + 3];
                    }
                }
                var count = (bottom - top) * (right - left);
                for (var y = top; y < bottom; y++)
                {
                    for (var x = left; x < right; x++)
                    {
                        var index = (y * Width + x) * 4;
                        Bytes[index] = (byte)(r / count);
                        Bytes[index + 1] = (byte)(g / count);
                        Bytes[index + 2] = (byte)(b / count);
                        Bytes[index + 3] = (byte)(a / count);
                    }
                }
            }
        }
    }

    /// <summary>Fine grey noise, the same for the same canvas position every time it is
    /// drawn, so a blur does not shimmer while it is dragged. <paramref name="origin"/> is
    /// where this buffer sits on the canvas. The hash is the Mac's, bit for bit.</summary>
    public void AddNoise(int amplitude, (int X, int Y) origin)
    {
        var span = (uint)(amplitude * 2 + 1);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                uint hash = unchecked((uint)(((long)(x + origin.X) * 73_856_093) ^ ((long)(y + origin.Y) * 19_349_663)));
                hash ^= hash >> 13;
                hash = unchecked(hash * 0x5BD1_E995);
                hash ^= hash >> 15;
                var noise = (int)(hash % span) - amplitude;
                var index = (y * Width + x) * 4;
                var alpha = (int)Bytes[index + 3];
                for (var channel = 0; channel < 3; channel++)
                    Bytes[index + channel] = (byte)Math.Max(0, Math.Min(alpha, Bytes[index + channel] + noise));
            }
        }
    }
}
