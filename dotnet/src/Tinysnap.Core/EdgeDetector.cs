using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>A capture's brightness, one byte a pixel, row 0 at the top. Read once per capture,
/// so the walks below cost nothing per pointer move. A byte rather than a double: a 5K capture
/// is 15 MB this way and eight times that the other.</summary>
public sealed class LuminanceBuffer
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] values;

    private LuminanceBuffer(int width, int height, byte[] values)
    {
        Width = width;
        Height = height;
        this.values = values;
    }

    public static LuminanceBuffer? From(SKImage image)
    {
        if (PixelBuffer.From(image) is not { } pixels) return null;
        var values = new byte[pixels.Width * pixels.Height];
        for (var index = 0; index < values.Length; index++)
        {
            var at = index * 4;
            double r = pixels.Bytes[at], g = pixels.Bytes[at + 1], b = pixels.Bytes[at + 2];
            values[index] = (byte)Geometry.Round(0.2126 * r + 0.7152 * g + 0.0722 * b);
        }
        return new LuminanceBuffer(pixels.Width, pixels.Height, values);
    }

    /// <summary>0 for black to 1 for white.</summary>
    public double Luminance(int x, int y) => values[y * Width + x] / 255.0;
}

/// <summary>Finds where the region under a point ends, by walking out from it until the
/// brightness differs from the point's by at least <c>Threshold</c> and stays different for
/// <c>RunLength</c> pixels.</summary>
public readonly struct EdgeDetector
{
    public enum Direction { Left, Right, Up, Down }

    /// <summary>Brightness difference, 0 to 1, that counts as an edge.</summary>
    public double Threshold { get; }

    /// <summary>How many pixels in a row must stay changed. Rejects noise and soft shadows.</summary>
    public int RunLength { get; }

    public EdgeDetector(double threshold, int runLength)
    {
        Threshold = threshold;
        RunLength = runLength;
    }

    /// <summary>An edge counts once it has held for one point, however many pixels that is. A
    /// fixed run of three pixels was a point and a half on a capture of scale 2, and the
    /// hairline between two table rows, a point thick, could never satisfy it.</summary>
    public EdgeDetector(double threshold, double scale) : this(threshold, Math.Max(1, (int)Geometry.Round(scale))) { }

    /// <summary>The last pixel still in the region along <paramref name="direction"/>, or null
    /// when the region runs to the capture's border without an edge.</summary>
    public int? FirstEdge((int X, int Y) origin, Direction direction, LuminanceBuffer buffer)
    {
        var (dx, dy) = Step(direction);
        var reference = buffer.Luminance(origin.X, origin.Y);
        var (x, y) = origin;
        while (true)
        {
            int nextX = x + dx, nextY = y + dy;
            if (nextX < 0 || nextY < 0 || nextX >= buffer.Width || nextY >= buffer.Height) return null;
            if (Math.Abs(buffer.Luminance(nextX, nextY) - reference) >= Threshold
                && Holds((nextX, nextY), direction, reference, buffer))
                return direction is Direction.Left or Direction.Right ? x : y;
            x = nextX;
            y = nextY;
        }
    }

    /// <summary>The region around <paramref name="origin"/>, in pixels, closed by the capture's
    /// border wherever no edge closes it. Null on a flat field, where no walk finds anything.</summary>
    public Rect? Bounds((int X, int Y) origin, LuminanceBuffer buffer)
    {
        var left = FirstEdge(origin, Direction.Left, buffer);
        var right = FirstEdge(origin, Direction.Right, buffer);
        var top = FirstEdge(origin, Direction.Up, buffer);
        var bottom = FirstEdge(origin, Direction.Down, buffer);
        if (left is null && right is null && top is null && bottom is null) return null;
        int x = left ?? 0, y = top ?? 0;
        return new Rect(x, y, (right ?? buffer.Width - 1) - x + 1, (bottom ?? buffer.Height - 1) - y + 1);
    }

    /// <summary>A single stray pixel, or an antialiased fringe, is not an edge.</summary>
    private bool Holds((int X, int Y) start, Direction direction, double reference, LuminanceBuffer buffer)
    {
        var (dx, dy) = Step(direction);
        for (var offset = 0; offset < RunLength; offset++)
        {
            int x = start.X + dx * offset, y = start.Y + dy * offset;
            if (x < 0 || y < 0 || x >= buffer.Width || y >= buffer.Height) return true;
            if (Math.Abs(buffer.Luminance(x, y) - reference) < Threshold) return false;
        }
        return true;
    }

    private static (int Dx, int Dy) Step(Direction direction) => direction switch
    {
        Direction.Left => (-1, 0),
        Direction.Right => (1, 0),
        Direction.Up => (0, -1),
        _ => (0, 1),
    };
}
