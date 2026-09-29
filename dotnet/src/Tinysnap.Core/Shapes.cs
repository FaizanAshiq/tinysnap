using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>A straight arrow whose body widens from a thin tail to a swept back head. The
/// renderer strokes the outline with round joins as well as filling it, which rounds the
/// tip, the barbs and the tail instead of leaving sharp peaks.</summary>
internal static class ArrowShape
{
    /// <summary>Half the head's width, which is how far the arrow reaches either side of its line.</summary>
    public static double HeadHalfWidth(double width, double length) => HeadLength(width, length) * 0.62;

    private static double HeadLength(double width, double length) => Math.Min(length * 0.5, width * 4.2);

    public static SKPath Path(Point tail, Point tip, double width)
    {
        var path = new SKPath();
        var length = tail.Distance(tip);
        if (length <= 0) return path;

        var ux = (tip.X - tail.X) / length;
        var uy = (tip.Y - tail.Y) / length;
        var nx = -uy;
        var ny = ux;
        var head = HeadLength(width, length);
        var headHalf = HeadHalfWidth(width, length);
        var neckHalf = Math.Min(width * 0.6, headHalf * 0.5);
        var tailHalf = width * 0.18;
        var baseAt = new Point(tip.X - ux * head, tip.Y - uy * head);
        // The barbs sit a little behind the neck, which sweeps the head back.
        var barb = new Point(baseAt.X - ux * head * 0.12, baseAt.Y - uy * head * 0.12);

        SKPoint At(Point point, double offset) => new((float)(point.X + nx * offset), (float)(point.Y + ny * offset));

        path.AddPoly(
        [
            At(tail, tailHalf), At(baseAt, neckHalf), At(barb, headHalf), tip.ToSK(),
            At(barb, -headHalf), At(baseAt, -neckHalf), At(tail, -tailHalf),
        ], close: true);
        return path;
    }
}

/// <summary>A smooth curve through a freehand stroke's recorded points, Catmull-Rom style.</summary>
internal static class Smoothing
{
    public static SKPath Path(IReadOnlyList<Point> points)
    {
        var path = new SKPath();
        if (points.Count == 0) return path;
        path.MoveTo(points[0].ToSK());
        if (points.Count <= 2)
        {
            foreach (var point in points.Skip(1)) path.LineTo(point.ToSK());
            return path;
        }
        for (var index = 0; index < points.Count - 1; index++)
        {
            var p0 = points[Math.Max(index - 1, 0)];
            var p1 = points[index];
            var p2 = points[index + 1];
            var p3 = points[Math.Min(index + 2, points.Count - 1)];
            path.CubicTo(
                new SKPoint((float)(p1.X + (p2.X - p0.X) / 6), (float)(p1.Y + (p2.Y - p0.Y) / 6)),
                new SKPoint((float)(p2.X - (p3.X - p1.X) / 6), (float)(p2.Y - (p3.Y - p1.Y) / 6)),
                p2.ToSK());
        }
        return path;
    }
}
