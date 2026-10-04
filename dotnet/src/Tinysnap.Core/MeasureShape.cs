using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>How a measurement is drawn: the line, a tick across each end, and its length on
/// a tag at the middle. The renderer and the Measure tool's live reading both draw through
/// here, so the reading looks exactly like what a click keeps.</summary>
public static partial class MeasureShape
{
    /// <summary>Tick length and label size follow the line's width, all in points: an eight
    /// point tick at the medium two point line.</summary>
    internal static double TickLength(double width) => 4 + width * 2;

    internal static double LabelPoints(double width) => 9 + width * 1.5;

    /// <summary>The tag's box and its text, in capture pixels. On the line's middle when the
    /// line has room for it with both ends showing; beside the line when it does not, above a
    /// line running across and right of one running down, since a small gap is often
    /// narrower than its own label and a centred tag hid what was measured.
    /// <paramref name="at"/> is where along the line the tag sits, 0.5 unless it slid clear
    /// of another.</summary>
    internal static (Rect Rect, string Text, double Points) Tag(Point from, Point to, double width, double scale,
                                                                double at = 0.5)
    {
        var length = from.Distance(to);
        var text = MeasureReading.Label(length, scale);
        var points = LabelPoints(width);
        var metrics = TextLayout.Metrics(text, points, scale, bold: true);
        var size = new Size(metrics.Width + 10 * scale, metrics.Ascent + metrics.Descent + 6 * scale);
        var middle = new Point(from.X + (to.X - from.X) * at, from.Y + (to.Y - from.Y) * at);
        if (length > 0)
        {
            // Pointing right, or down when upright, so beside means above or to the right.
            var along = new Vector((to.X - from.X) / length, (to.Y - from.Y) / length);
            if (along.Dx < 0 || (along.Dx == 0 && along.Dy < 0)) along = new Vector(-along.Dx, -along.Dy);
            var tick = TickLength(width) * scale;
            var alongExtent = Math.Abs(along.Dx) * size.Width + Math.Abs(along.Dy) * size.Height;
            if (length < alongExtent + tick * 2)
            {
                var beside = new Vector(along.Dy, -along.Dx);
                var away = tick / 2 + 2 * scale + Math.Abs(beside.Dx) * size.Width / 2 + Math.Abs(beside.Dy) * size.Height / 2;
                middle = new Point(middle.X + beside.Dx * away, middle.Y + beside.Dy * away);
            }
        }
        return (new Rect(middle.X - size.Width / 2, middle.Y - size.Height / 2, size.Width, size.Height), text, points);
    }

    /// <summary>Everything drawn, ticks and tag included, for the selection outline.</summary>
    internal static Rect Extent(Point from, Point to, double width, double scale, double at = 0.5)
    {
        var tick = TickLength(width) * scale / 2;
        return Rect.FromCorners(from, to).Inset(-tick, -tick).Union(Tag(from, to, width, scale, at).Rect);
    }
}

public static partial class MeasureShape
{
    /// <summary>The lines of one reading with their tags clear of each other and of each
    /// other's line. Across and down through the middle of a card put both tags on the
    /// crossing, so each tag first slides along its own line off the other line, to whichever
    /// side is nearer its middle, and then the later one slides again if the two still meet.</summary>
    public static IReadOnlyList<MeasureLine> ClearTags(IReadOnlyList<MeasureLine> lines, double width, double scale)
    {
        if (lines.Count != 2) return lines;
        var placed = lines.ToArray();
        Rect TagRect(MeasureLine line) => Tag(line.From, line.To, width, scale, line.LabelAt).Rect;
        Rect StrokeRect(MeasureLine line) => Rect.FromCorners(line.From, line.To).Inset(-width * scale / 2, -width * scale / 2);
        foreach (var (index, other) in new[] { (0, 1), (1, 0) })
        {
            if (!TagRect(placed[index]).Intersects(StrokeRect(placed[other]))) continue;
            if (Slide(placed[index], StrokeRect(placed[other]), width, scale) is { } moved) placed[index] = moved;
        }
        if (!TagRect(placed[0]).Intersects(TagRect(placed[1]))) return placed;
        if (Slide(placed[1], TagRect(placed[0]), width, scale) is { } second) placed[1] = second;
        else if (Slide(placed[0], TagRect(placed[1]), width, scale) is { } first) placed[0] = first;
        return placed;
    }

    /// <summary>The line with its tag moved along it until it just clears
    /// <paramref name="other"/>, on the side nearer the line's middle. Null when neither side
    /// leaves the tag on the line with a tick's room at each end.</summary>
    private static MeasureLine? Slide(MeasureLine line, Rect other, double width, double scale)
    {
        var length = line.From.Distance(line.To);
        if (length <= 0) return null;
        var along = new Vector((line.To.X - line.From.X) / length, (line.To.Y - line.From.Y) / length);
        var size = Tag(line.From, line.To, width, scale).Rect.Size;
        var extent = Math.Abs(along.Dx) * size.Width + Math.Abs(along.Dy) * size.Height;
        // The other tag's shadow on this line, as distances from its start.
        Point[] corners =
        [
            new(other.MinX, other.MinY), new(other.MaxX, other.MinY),
            new(other.MinX, other.MaxY), new(other.MaxX, other.MaxY),
        ];
        var shadow = corners.Select(p => (p.X - line.From.X) * along.Dx + (p.Y - line.From.Y) * along.Dy).ToArray();
        var gap = 4 * scale;
        var room = extent / 2 + TickLength(width) * scale;
        var candidates = new[] { shadow.Min() - gap - extent / 2, shadow.Max() + gap + extent / 2 }
            .Where(d => d >= room && d <= length - room)
            .OrderBy(d => Math.Abs(d - length / 2));
        foreach (var distance in candidates)
        {
            var moved = line with { LabelAt = distance / length };
            if (!Tag(moved.From, moved.To, width, scale, moved.LabelAt).Rect.Intersects(other)) return moved;
        }
        return null;
    }

    /// <summary><paramref name="width"/> is the line's width in points. The canvas is in
    /// capture pixels, y down.</summary>
    public static void Draw(SKCanvas canvas, Point from, Point to, double width, SKColor color, double scale,
                            double at = 0.5)
    {
        var length = from.Distance(to);
        if (length <= 0) return;
        using (var stroke = new SKPaint
               {
                   Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke,
                   StrokeWidth = (float)(width * scale), StrokeCap = SKStrokeCap.Butt,
               })
        {
            // Each tick crosses its end at a right angle to the line.
            var half = TickLength(width) * scale / 2;
            var across = new Vector(-(to.Y - from.Y) / length * half, (to.X - from.X) / length * half);
            canvas.DrawLine(from.ToSK(), to.ToSK(), stroke);
            foreach (var end in new[] { from, to })
                canvas.DrawLine(new SKPoint((float)(end.X - across.Dx), (float)(end.Y - across.Dy)),
                                new SKPoint((float)(end.X + across.Dx), (float)(end.Y + across.Dy)), stroke);
        }

        var tag = Tag(from, to, width, scale, at);
        using (var fill = new SKPaint { Color = color, IsAntialias = true })
            canvas.DrawRoundRect(tag.Rect.ToSK(), (float)(4 * scale), (float)(4 * scale), fill);
        var metrics = TextLayout.Metrics(tag.Text, tag.Points, scale, bold: true);
        var origin = new Point(tag.Rect.MidX - metrics.Width / 2, tag.Rect.MidY - (metrics.Ascent + metrics.Descent) / 2);
        TextLayout.Draw(canvas, tag.Text, origin, tag.Points, scale, TextColor(color), bold: true);
    }

    /// <summary>White on a dark colour, black on a light one, so the length always reads.</summary>
    internal static SKColor TextColor(SKColor color) => Palette.Luminance(color) > 0.6 ? SKColors.Black : SKColors.White;
}
