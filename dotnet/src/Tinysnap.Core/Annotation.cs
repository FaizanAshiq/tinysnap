using System.Collections.Immutable;
using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>An image pasted onto a capture. Compared by identity, as the same pasted
/// pixels are shared by every undo snapshot.</summary>
public sealed class PastedImage(SKImage image)
{
    public SKImage Image { get; } = image;
}

/// <summary>Every kind of annotation and the geometry it keeps, in capture pixels.</summary>
public abstract record AnnotationKind
{
    private AnnotationKind() { }

    public sealed record Arrow(Point From, Point To) : AnnotationKind;
    public sealed record Line(Point From, Point To) : AnnotationKind;
    public sealed record Rectangle(Rect Rect) : AnnotationKind;
    public sealed record Oval(Rect Rect) : AnnotationKind;
    public sealed record Text(Point Origin, string String) : AnnotationKind;
    public sealed record Highlighter(Point From, Point To) : AnnotationKind;

    public sealed record Freehand(ImmutableArray<Point> Points) : AnnotationKind
    {
        // An immutable array compares by reference; two strokes with the same points are
        // the same stroke, which undo and the tests rely on.
        public bool Equals(Freehand? other) => other is not null && Points.SequenceEqual(other.Points);
        public override int GetHashCode() => Points.Aggregate(0, (hash, p) => HashCode.Combine(hash, p));
    }

    public sealed record Step(Point Center) : AnnotationKind;
    public sealed record Spotlight(Rect Rect) : AnnotationKind;
    public sealed record Magnifier(Point Center, double Radius, double Zoom) : AnnotationKind;
    public sealed record Image(Rect Rect, PastedImage Pasted) : AnnotationKind;
    public sealed record Blur(Rect Rect) : AnnotationKind;
    public sealed record Pixelate(Rect Rect) : AnnotationKind;
    public sealed record Erase(Rect Rect) : AnnotationKind;

    /// <summary>A kept measurement: the line, its end ticks and its length in points.</summary>
    public sealed record Measure(Point From, Point To) : AnnotationKind;
}

public sealed record Annotation(Guid Id, AnnotationKind Kind, Style Style, double LabelAt = 0.5)
{
    // LabelAt: where a measurement's tag sits along its line, 0 at `from` and 1 at `to`.
    // Every other kind ignores it.

    /// <summary>Locked: nothing about it changes until it is unlocked, and drawing tools draw
    /// over it.</summary>
    public bool IsLocked { get; init; }

    /// <summary>Hidden: kept, but not drawn, clicked, copied or saved into an image.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Its place in the order the shapes were drawn, from 1, which numbers its name in
    /// the layers list: Rectangle 1, Rectangle 2. 0 until a document gives it one.</summary>
    public int Serial { get; init; }

    public static Annotation New(AnnotationKind kind, Style style, double labelAt = 0.5) =>
        new(Guid.NewGuid(), kind, style, labelAt);

    public Tool Tool => Kind switch
    {
        AnnotationKind.Arrow => Tool.Arrow,
        AnnotationKind.Line => Tool.Line,
        AnnotationKind.Rectangle => Tool.Rectangle,
        AnnotationKind.Oval => Tool.Oval,
        AnnotationKind.Text => Tool.Text,
        AnnotationKind.Highlighter => Tool.Highlighter,
        AnnotationKind.Freehand => Tool.Freehand,
        AnnotationKind.Step => Tool.Step,
        AnnotationKind.Spotlight => Tool.Spotlight,
        AnnotationKind.Magnifier => Tool.Magnifier,
        AnnotationKind.Image => Tool.Image,
        AnnotationKind.Blur => Tool.Blur,
        AnnotationKind.Pixelate => Tool.Pixelate,
        AnnotationKind.Erase => Tool.Erase,
        _ => Tool.Measure,
    };

    /// <summary>The style's size in capture pixels: a stroke width, a font size, a step's
    /// diameter, a blur radius or a pixelate block.</summary>
    public double PixelSize(double scale) => (Tool.Points(Style.Size) ?? 0) * scale;

    /// <summary>The box used for the selection outline, in capture pixels.</summary>
    public Rect Bounds(double scale)
    {
        var size = PixelSize(scale);
        switch (Kind)
        {
            case AnnotationKind.Arrow(var from, var to):
                var reach = ArrowShape.HeadHalfWidth(size, from.Distance(to));
                return Rect.FromCorners(from, to).Inset(-reach, -reach);
            case AnnotationKind.Line(var from, var to):
                return Rect.FromCorners(from, to).Inset(-size / 2, -size / 2);
            case AnnotationKind.Highlighter(var from, var to):
                return Rect.FromCorners(from, to).Inset(-size / 2, -size / 2);
            case AnnotationKind.Measure(var from, var to):
                return MeasureShape.Extent(from, to, size / scale, scale, LabelAt);
            case AnnotationKind.Text(var origin, var text):
                return new Rect(origin, TextLayout.Size(text, size / scale, scale));
            case AnnotationKind.Freehand(var points):
                if (points.IsEmpty) return Rect.Null;
                var box = points.Aggregate(new Rect(points[0], Size.Zero), (box, p) => box.Union(new Rect(p, Size.Zero)));
                return box.Inset(-size / 2, -size / 2);
            case AnnotationKind.Step(var center):
                return new Rect(center.X - size / 2, center.Y - size / 2, size, size);
            case AnnotationKind.Magnifier(var center, var radius, _):
                return new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            default:
                return BoxOf(Kind)!.Value;
        }
    }

    /// <summary>The box of every kind that is a box.</summary>
    private static Rect? BoxOf(AnnotationKind kind) => kind switch
    {
        AnnotationKind.Rectangle(var rect) => rect,
        AnnotationKind.Oval(var rect) => rect,
        AnnotationKind.Spotlight(var rect) => rect,
        AnnotationKind.Blur(var rect) => rect,
        AnnotationKind.Pixelate(var rect) => rect,
        AnnotationKind.Erase(var rect) => rect,
        AnnotationKind.Image(var rect, _) => rect,
        _ => null,
    };

    /// <summary>Whether a click at <paramref name="point"/> lands on this annotation. Thin
    /// strokes get at least four points of reach, or a 2 pixel line could never be picked up.</summary>
    public bool Contains(Point point, double scale)
    {
        var size = PixelSize(scale);
        var reach = Math.Max(size / 2, 4 * scale);
        switch (Kind)
        {
            case AnnotationKind.Arrow(var from, var to):
                var head = ArrowShape.HeadHalfWidth(size, from.Distance(to));
                return point.DistanceToSegment(from, to) <= Math.Max(reach, head);
            case AnnotationKind.Line(var from, var to):
                return point.DistanceToSegment(from, to) <= reach;
            case AnnotationKind.Highlighter(var from, var to):
                return point.DistanceToSegment(from, to) <= reach;
            case AnnotationKind.Measure(var from, var to):
                // The line, or its label, which is the easiest part of it to aim at.
                return point.DistanceToSegment(from, to) <= reach
                    || MeasureShape.Tag(from, to, size / scale, scale, LabelAt).Rect.Contains(point);
            case AnnotationKind.Rectangle(var rect):
            {
                if (Style.Filled) return rect.Inset(-reach, -reach).Contains(point);
                var inner = rect.Inset(reach, reach);
                var inside = !inner.IsNull && inner.Contains(point);
                return rect.Inset(-reach, -reach).Contains(point) && !inside;
            }
            case AnnotationKind.Oval(var rect):
            {
                if (Style.Filled) return EllipseContains(rect.Inset(-reach, -reach), point);
                var inner = rect.Inset(reach, reach);
                var inside = !inner.IsNull && inner.Width > 0 && inner.Height > 0 && EllipseContains(inner, point);
                return EllipseContains(rect.Inset(-reach, -reach), point) && !inside;
            }
            case AnnotationKind.Text or AnnotationKind.Step:
                return Bounds(scale).Inset(-reach, -reach).Contains(point);
            case AnnotationKind.Freehand(var points):
                if (points.Length == 1) return point.Distance(points[0]) <= reach;
                return points.Zip(points.Skip(1)).Any(pair => point.DistanceToSegment(pair.First, pair.Second) <= reach);
            case AnnotationKind.Magnifier(var center, var radius, _):
                return point.Distance(center) <= radius;
            default:
                return BoxOf(Kind)!.Value.Contains(point);
        }
    }

    private static bool EllipseContains(Rect rect, Point point)
    {
        if (rect.IsNull || rect.Width <= 0 || rect.Height <= 0) return false;
        var x = (point.X - rect.MidX) / (rect.Size.Width / 2);
        var y = (point.Y - rect.MidY) / (rect.Size.Height / 2);
        return x * x + y * y <= 1;
    }

    public Annotation Moved(Vector vector)
    {
        Rect Shift(Rect rect) => rect.Offset(vector.Dx, vector.Dy);
        AnnotationKind kind = Kind switch
        {
            AnnotationKind.Arrow(var from, var to) => new AnnotationKind.Arrow(from.Offset(vector), to.Offset(vector)),
            AnnotationKind.Line(var from, var to) => new AnnotationKind.Line(from.Offset(vector), to.Offset(vector)),
            AnnotationKind.Highlighter(var from, var to) => new AnnotationKind.Highlighter(from.Offset(vector), to.Offset(vector)),
            AnnotationKind.Measure(var from, var to) => new AnnotationKind.Measure(from.Offset(vector), to.Offset(vector)),
            AnnotationKind.Rectangle(var rect) => new AnnotationKind.Rectangle(Shift(rect)),
            AnnotationKind.Oval(var rect) => new AnnotationKind.Oval(Shift(rect)),
            AnnotationKind.Spotlight(var rect) => new AnnotationKind.Spotlight(Shift(rect)),
            AnnotationKind.Blur(var rect) => new AnnotationKind.Blur(Shift(rect)),
            AnnotationKind.Pixelate(var rect) => new AnnotationKind.Pixelate(Shift(rect)),
            AnnotationKind.Erase(var rect) => new AnnotationKind.Erase(Shift(rect)),
            AnnotationKind.Image(var rect, var image) => new AnnotationKind.Image(Shift(rect), image),
            AnnotationKind.Text(var origin, var text) => new AnnotationKind.Text(origin.Offset(vector), text),
            AnnotationKind.Freehand(var points) => new AnnotationKind.Freehand([.. points.Select(p => p.Offset(vector))]),
            AnnotationKind.Step(var center) => new AnnotationKind.Step(center.Offset(vector)),
            AnnotationKind.Magnifier(var center, var radius, var zoom) => new AnnotationKind.Magnifier(center.Offset(vector), radius, zoom),
            _ => Kind,
        };
        return this with { Kind = kind };
    }

    /// <summary>Where the resize handles sit. Text, steps and freehand strokes only move.</summary>
    public IReadOnlyList<(Handle Handle, Point Point)> Handles(double scale) => Kind switch
    {
        AnnotationKind.Arrow(var from, var to) => [(Handle.Start, from), (Handle.End, to)],
        AnnotationKind.Line(var from, var to) => [(Handle.Start, from), (Handle.End, to)],
        AnnotationKind.Highlighter(var from, var to) => [(Handle.Start, from), (Handle.End, to)],
        AnnotationKind.Measure(var from, var to) => [(Handle.Start, from), (Handle.End, to)],
        AnnotationKind.Magnifier => [.. Bounds(scale).HandlePoints.Take(4)],
        AnnotationKind.Text or AnnotationKind.Freehand or AnnotationKind.Step => [],
        _ => BoxOf(Kind)!.Value.HandlePoints,
    };

    /// <summary>This annotation with one handle dragged to <paramref name="point"/>.
    /// <paramref name="constrained"/> is Shift: 45 degree steps for lines, squares for boxes.</summary>
    public Annotation Resized(Handle handle, Point point, bool constrained)
    {
        (Point, Point) Line(Point from, Point to) => handle switch
        {
            Handle.Start => (constrained ? point.Snapped45(to) : point, to),
            Handle.End => (from, constrained ? point.Snapped45(from) : point),
            _ => (from, to),
        };
        Rect Box(Rect rect) => rect.Resized(handle, point, constrained);

        AnnotationKind kind;
        switch (Kind)
        {
            case AnnotationKind.Arrow(var from, var to): { var (a, b) = Line(from, to); kind = new AnnotationKind.Arrow(a, b); break; }
            case AnnotationKind.Line(var from, var to): { var (a, b) = Line(from, to); kind = new AnnotationKind.Line(a, b); break; }
            case AnnotationKind.Highlighter(var from, var to): { var (a, b) = Line(from, to); kind = new AnnotationKind.Highlighter(a, b); break; }
            case AnnotationKind.Measure(var from, var to): { var (a, b) = Line(from, to); kind = new AnnotationKind.Measure(a, b); break; }
            case AnnotationKind.Rectangle(var rect): kind = new AnnotationKind.Rectangle(Box(rect)); break;
            case AnnotationKind.Oval(var rect): kind = new AnnotationKind.Oval(Box(rect)); break;
            case AnnotationKind.Spotlight(var rect): kind = new AnnotationKind.Spotlight(Box(rect)); break;
            case AnnotationKind.Blur(var rect): kind = new AnnotationKind.Blur(Box(rect)); break;
            case AnnotationKind.Pixelate(var rect): kind = new AnnotationKind.Pixelate(Box(rect)); break;
            case AnnotationKind.Erase(var rect): kind = new AnnotationKind.Erase(Box(rect)); break;
            case AnnotationKind.Image(var rect, var image): kind = new AnnotationKind.Image(Box(rect), image); break;
            case AnnotationKind.Magnifier(var center, _, var zoom):
                var radius = Math.Max(Math.Max(Math.Abs(point.X - center.X), Math.Abs(point.Y - center.Y)), 8);
                kind = new AnnotationKind.Magnifier(center, radius, zoom);
                break;
            default:
                kind = Kind;
                break;
        }
        return this with { Kind = kind };
    }

    /// <summary>Too small to keep. A click without a drag makes these, and they would be
    /// invisible objects the user never meant to create.</summary>
    public bool IsDegenerate(double scale)
    {
        var minimum = 2 * scale;
        return Kind switch
        {
            AnnotationKind.Arrow(var from, var to) => from.Distance(to) < minimum,
            AnnotationKind.Line(var from, var to) => from.Distance(to) < minimum,
            AnnotationKind.Highlighter(var from, var to) => from.Distance(to) < minimum,
            AnnotationKind.Measure(var from, var to) => from.Distance(to) < minimum,
            AnnotationKind.Freehand(var points) => points.Length < 2,
            AnnotationKind.Text(_, var text) => string.IsNullOrWhiteSpace(text),
            AnnotationKind.Step or AnnotationKind.Magnifier => false,
            _ => BoxOf(Kind)!.Value is var rect && (rect.Size.Width < minimum || rect.Size.Height < minimum),
        };
    }
}
