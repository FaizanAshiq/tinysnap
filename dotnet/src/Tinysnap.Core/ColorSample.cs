using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>The colours a capture's surroundings are painted with, read from a 64 pixel
/// copy, so even a 5K capture costs one quick downsample. Pixels are grouped by colour and
/// the biggest group wins, which ignores a toolbar or a photo in one corner. Transparent
/// pixels, such as a window's rounded corners, are left out.</summary>
internal sealed class ColorSample
{
    public SKColor Edge { get; }
    public SKColor[] Gradient { get; }

    private const int Side = 64;

    /// <summary>How far apart, in 0 to 255 per channel, two colours are to read as different.</summary>
    private const double Different = 80;

    public ColorSample(SKImage image)
    {
        var white = new SKColor(255, 255, 255);
        var pixels = Pixels(image);
        Edge = Groups(pixels.Where(p => p.OnBorder)).FirstOrDefault()?.Color ?? white;
        var groups = Groups(pixels);
        if (groups.Count == 0)
        {
            Gradient = [white, white];
            return;
        }
        var first = groups[0];
        var second = groups.Skip(1).FirstOrDefault(g => g.Distance(first) >= Different);
        Gradient = [first.Color, second?.Color ?? first.Shade];
    }

    private readonly record struct Pixel(int Red, int Green, int Blue, bool OnBorder);

    private sealed class Group
    {
        public int Count;
        public int Red, Green, Blue;

        private (double R, double G, double B) Mean
        {
            get
            {
                var count = (double)Math.Max(1, Count);
                return (Red / count, Green / count, Blue / count);
            }
        }

        public SKColor Color
        {
            get
            {
                var (r, g, b) = Mean;
                return new SKColor((byte)Geometry.Round(r), (byte)Geometry.Round(g), (byte)Geometry.Round(b));
            }
        }

        public double Distance(Group other)
        {
            var (a, b) = (Mean, other.Mean);
            return Math.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));
        }

        /// <summary>A light colour gets a darker shade and anything else a lighter one, so the
        /// gradient is always visible.</summary>
        public SKColor Shade
        {
            get
            {
                var (red, green, blue) = Mean;
                var light = (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255 > 0.8;
                byte Mix(double channel) =>
                    (byte)Geometry.Round(light ? channel * 0.7 : channel + (255 - channel) * 0.4);
                return new SKColor(Mix(red), Mix(green), Mix(blue));
            }
        }
    }

    private static List<Pixel> Pixels(SKImage image)
    {
        var info = new SKImageInfo(Side, Side, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        if (surface is null) return [];
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(image, new SKRect(0, 0, Side, Side),
                                 new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var snapshot = surface.Snapshot();
        var buffer = PixelBuffer.From(snapshot);
        if (buffer is null) return [];
        var pixels = new List<Pixel>(Side * Side);
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                var (r, g, b, a) = buffer.Pixel(x, y);
                if (a < 128) continue;
                // Premultiplied, so each channel is divided back out.
                pixels.Add(new Pixel(r * 255 / a, g * 255 / a, b * 255 / a,
                                     y == 0 || y == Side - 1 || x == 0 || x == Side - 1));
            }
        }
        return pixels;
    }

    /// <summary>Biggest first, and equal sizes by colour: a dictionary's order is not a
    /// promise, and following it could flip a capture's gradient on reopening.</summary>
    private static List<Group> Groups(IEnumerable<Pixel> pixels)
    {
        var groups = new Dictionary<int, Group>();
        foreach (var pixel in pixels)
        {
            var key = (pixel.Red >> 4) << 8 | (pixel.Green >> 4) << 4 | pixel.Blue >> 4;
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = new Group();
            group.Count++;
            group.Red += pixel.Red;
            group.Green += pixel.Green;
            group.Blue += pixel.Blue;
        }
        return groups.OrderByDescending(p => p.Value.Count).ThenBy(p => p.Key).Select(p => p.Value).ToList();
    }
}
