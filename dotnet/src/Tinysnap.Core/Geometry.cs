using SkiaSharp;

namespace Tinysnap.Core;

// Every coordinate in Tinysnap.Core is in capture pixels with y growing downward, the
// way the captured image is stored. Skia shares that space, so nothing here, and nothing
// that draws with it, ever flips.

/// <summary>A grab point on a selected annotation or on the crop box.</summary>
public enum Handle
{
    Start, End,
    TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left,
}

public static class Geometry
{
    /// <summary>Halves away from zero, as Swift's <c>rounded()</c> does. C#'s default rounds
    /// halves to even, which would move a pixel the Mac keeps.</summary>
    public static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}

public readonly record struct Point(double X, double Y)
{
    public static readonly Point Zero = new(0, 0);

    public double Distance(Point other) => Math.Sqrt((other.X - X) * (other.X - X) + (other.Y - Y) * (other.Y - Y));

    public Point Offset(Vector vector) => new(X + vector.Dx, Y + vector.Dy);

    /// <summary>This point moved onto the nearest of the eight 45 degree directions from
    /// <paramref name="origin"/>. Projected rather than rotated, and written without
    /// trigonometry, so a 100 pixel drag along an axis stays exactly 100 instead of picking
    /// up the hypotenuse or a rounding error.</summary>
    public Point Snapped45(Point origin)
    {
        var dx = X - origin.X;
        var dy = Y - origin.Y;
        var ax = Math.Abs(dx);
        var ay = Math.Abs(dy);
        const double tan22 = 0.41421356237;

        if (ay <= ax * tan22) return new Point(origin.X + dx, origin.Y);
        if (ax <= ay * tan22) return new Point(origin.X, origin.Y + dy);

        var diagonal = (ax + ay) / 2;
        return new Point(origin.X + (dx < 0 ? -diagonal : diagonal), origin.Y + (dy < 0 ? -diagonal : diagonal));
    }

    /// <summary>The corner that makes a square with <paramref name="anchor"/>, keeping the
    /// drag's direction.</summary>
    public Point Squared(Point anchor) => Fitted(anchor, 1);

    /// <summary>This point moved so the box from <paramref name="anchor"/> to it has
    /// <paramref name="ratio"/>, width over height, on the drag's longer side. A drag taller than
    /// it is wide turns the ratio upright.</summary>
    public Point Fitted(Point anchor, double ratio)
    {
        var dx = X - anchor.X;
        var dy = Y - anchor.Y;
        var shape = Math.Abs(dx) >= Math.Abs(dy) ? ratio : 1 / ratio;
        var width = Math.Max(Math.Abs(dx), Math.Abs(dy) * shape);
        var height = width / shape;
        return new Point(anchor.X + (dx < 0 ? -width : width), anchor.Y + (dy < 0 ? -height : height));
    }

    public double DistanceToSegment(Point a, Point b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var lengthSquared = abx * abx + aby * aby;
        if (lengthSquared <= 0) return Distance(a);
        var t = Math.Max(0, Math.Min(1, ((X - a.X) * abx + (Y - a.Y) * aby) / lengthSquared));
        return Distance(new Point(a.X + t * abx, a.Y + t * aby));
    }

    public SKPoint ToSK() => new((float)X, (float)Y);
}

public readonly record struct Size(double Width, double Height)
{
    public static readonly Size Zero = new(0, 0);
}

public readonly record struct Vector(double Dx, double Dy)
{
    public static readonly Vector Zero = new(0, 0);
}

/// <summary>A rectangle with CGRect's rules: every read standardises a negative size, and
/// a failed intersection is the null rect, which differs from an empty one.</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public static readonly Rect Null = new(double.PositiveInfinity, double.PositiveInfinity, 0, 0);
    public static readonly Rect Zero = new(0, 0, 0, 0);

    public Rect(Point origin, Size size) : this(origin.X, origin.Y, size.Width, size.Height) { }

    public bool IsNull => double.IsPositiveInfinity(X) || double.IsPositiveInfinity(Y);
    public bool IsEmpty => IsNull || Width == 0 || Height == 0;

    /// <summary>Only the four numbers. The generated printer also prints every computed rect,
    /// such as <see cref="Standardized"/>, which prints its own, and never ends.</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"X = {X}, Y = {Y}, Width = {Width}, Height = {Height}");
        return true;
    }

    public Rect Standardized => IsNull
        ? this
        : new Rect(Width < 0 ? X + Width : X, Height < 0 ? Y + Height : Y, Math.Abs(Width), Math.Abs(Height));

    public double MinX => Width < 0 ? X + Width : X;
    public double MinY => Height < 0 ? Y + Height : Y;
    public double MaxX => MinX + Math.Abs(Width);
    public double MaxY => MinY + Math.Abs(Height);
    public double MidX => (MinX + MaxX) / 2;
    public double MidY => (MinY + MaxY) / 2;
    public Point Origin => new(MinX, MinY);
    public Size Size => new(Math.Abs(Width), Math.Abs(Height));
    public Point Center => new(MidX, MidY);

    /// <summary>The rectangle spanned by two corners, given in either order.</summary>
    public static Rect FromCorners(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    /// <summary>Grown outward to whole numbers.</summary>
    public Rect Integral
    {
        get
        {
            if (IsNull) return this;
            var left = Math.Floor(MinX);
            var top = Math.Floor(MinY);
            return new Rect(left, top, Math.Ceiling(MaxX) - left, Math.Ceiling(MaxY) - top);
        }
    }

    public Rect Intersection(Rect other)
    {
        if (IsNull || other.IsNull) return Null;
        var left = Math.Max(MinX, other.MinX);
        var top = Math.Max(MinY, other.MinY);
        var right = Math.Min(MaxX, other.MaxX);
        var bottom = Math.Min(MaxY, other.MaxY);
        return right < left || bottom < top ? Null : new Rect(left, top, right - left, bottom - top);
    }

    public bool Intersects(Rect other)
    {
        var meet = Intersection(other);
        return !meet.IsNull && !meet.IsEmpty;
    }

    public Rect Union(Rect other)
    {
        if (IsNull) return other;
        if (other.IsNull) return this;
        var left = Math.Min(MinX, other.MinX);
        var top = Math.Min(MinY, other.MinY);
        return new Rect(left, top, Math.Max(MaxX, other.MaxX) - left, Math.Max(MaxY, other.MaxY) - top);
    }

    public bool Contains(Point p) => !IsNull && p.X >= MinX && p.X < MaxX && p.Y >= MinY && p.Y < MaxY;

    public bool Contains(Rect r) =>
        !IsNull && !r.IsNull && r.MinX >= MinX && r.MaxX <= MaxX && r.MinY >= MinY && r.MaxY <= MaxY;

    public Rect Inset(double dx, double dy)
    {
        if (IsNull) return this;
        var width = Size.Width - dx * 2;
        var height = Size.Height - dy * 2;
        return width < 0 || height < 0 ? Null : new Rect(MinX + dx, MinY + dy, width, height);
    }

    public Rect Offset(double dx, double dy) => IsNull ? this : new Rect(MinX + dx, MinY + dy, Size.Width, Size.Height);

    /// <summary>The eight resize handles, corners first.</summary>
    public IReadOnlyList<(Handle Handle, Point Point)> HandlePoints =>
    [
        (Handle.TopLeft, new Point(MinX, MinY)),
        (Handle.TopRight, new Point(MaxX, MinY)),
        (Handle.BottomRight, new Point(MaxX, MaxY)),
        (Handle.BottomLeft, new Point(MinX, MaxY)),
        (Handle.Top, new Point(MidX, MinY)),
        (Handle.Right, new Point(MaxX, MidY)),
        (Handle.Bottom, new Point(MidX, MaxY)),
        (Handle.Left, new Point(MinX, MidY)),
    ];

    /// <summary>This rectangle with one handle dragged to <paramref name="point"/>. Always
    /// computed from the rectangle as it was when the drag began, so dragging a corner past
    /// the opposite one flips the box cleanly instead of the handle swapping meaning mid drag.</summary>
    public Rect Resized(Handle handle, Point point, bool square) => Resized(handle, point, square ? 1 : null);

    /// <summary>With a <paramref name="ratio"/>, width over height, a corner keeps it and an edge
    /// takes the other side along with it, about that side's middle.</summary>
    public Rect Resized(Handle handle, Point point, double? ratio)
    {
        var upright = Size.Width < Size.Height;
        switch (handle)
        {
            case Handle.TopLeft: return Corner(new Point(MaxX, MaxY), point, ratio);
            case Handle.TopRight: return Corner(new Point(MinX, MaxY), point, ratio);
            case Handle.BottomRight: return Corner(new Point(MinX, MinY), point, ratio);
            case Handle.BottomLeft: return Corner(new Point(MaxX, MinY), point, ratio);
            case Handle.Top or Handle.Bottom:
            {
                var edge = handle == Handle.Top
                    ? FromCorners(new Point(MinX, point.Y), new Point(MaxX, MaxY))
                    : FromCorners(new Point(MinX, MinY), new Point(MaxX, point.Y));
                if (ratio is not { } r) return edge;
                var width = edge.Size.Height * (upright ? 1 / r : r);
                return new Rect(MidX - width / 2, edge.MinY, width, edge.Size.Height);
            }
            case Handle.Left or Handle.Right:
            {
                var edge = handle == Handle.Left
                    ? FromCorners(new Point(point.X, MinY), new Point(MaxX, MaxY))
                    : FromCorners(new Point(MinX, MinY), new Point(point.X, MaxY));
                if (ratio is not { } r) return edge;
                var height = edge.Size.Width / (upright ? 1 / r : r);
                return new Rect(edge.MinX, MidY - height / 2, edge.Size.Width, height);
            }
            default: return this;
        }
    }

    private static Rect Corner(Point anchor, Point point, double? ratio) =>
        FromCorners(anchor, ratio is { } r ? point.Fitted(anchor, r) : point);

    /// <summary>The largest box of <paramref name="ratio"/> inside this one, about its middle,
    /// upright when this one is.</summary>
    public Rect Trimmed(double ratio)
    {
        var shape = Size.Width >= Size.Height ? ratio : 1 / ratio;
        var (width, height) = Size.Width / Size.Height > shape
            ? (Size.Height * shape, Size.Height)
            : (Size.Width, Size.Width / shape);
        return new Rect(MidX - width / 2, MidY - height / 2, width, height);
    }

    /// <summary>The box a drag from <paramref name="anchor"/> to <paramref name="point"/>
    /// draws, the way Photoshop does: from the corner, or with <paramref name="fromCentre"/>
    /// (Alt) out from the anchor as its centre, and square when <paramref name="square"/>
    /// (Shift). The editor and the capture overlay both draw with this.</summary>
    public static Rect Dragged(Point anchor, Point point, bool square, bool fromCentre) =>
        Dragged(anchor, point, square ? 1 : null, fromCentre);

    /// <summary>The same, keeping <paramref name="ratio"/>, width over height, when there is one.</summary>
    public static Rect Dragged(Point anchor, Point point, double? ratio, bool fromCentre)
    {
        var end = ratio is { } r ? point.Fitted(anchor, r) : point;
        if (!fromCentre) return FromCorners(anchor, end);
        var halfWidth = Math.Abs(end.X - anchor.X);
        var halfHeight = Math.Abs(end.Y - anchor.Y);
        return new Rect(anchor.X - halfWidth, anchor.Y - halfHeight, halfWidth * 2, halfHeight * 2);
    }

    /// <summary>How far this box can move by <paramref name="vector"/> and stay inside
    /// <paramref name="bounds"/>, for moving a box with Space held.</summary>
    public Vector AllowedMove(Vector vector, Rect bounds) =>
        new(Math.Min(Math.Max(vector.Dx, bounds.MinX - MinX), bounds.MaxX - MaxX),
            Math.Min(Math.Max(vector.Dy, bounds.MinY - MinY), bounds.MaxY - MaxY));

    /// <summary>Snapped outward to whole pixels, for the crop box.</summary>
    public Rect WholePixels =>
        FromCorners(new Point(Geometry.Round(MinX), Geometry.Round(MinY)), new Point(Geometry.Round(MaxX), Geometry.Round(MaxY)));

    public SKRect ToSK() => new((float)MinX, (float)MinY, (float)MaxX, (float)MaxY);

    public SKRectI ToSKRectI() => new((int)MinX, (int)MinY, (int)MaxX, (int)MaxY);
}
