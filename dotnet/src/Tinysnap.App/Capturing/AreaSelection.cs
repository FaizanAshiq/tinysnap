using System.Globalization;
using Tinysnap.Core;

namespace Tinysnap.App.Capturing;

/// <summary>The box being dragged on one monitor's overlay, in points from its top left corner,
/// with the editor's keys: Shift squares, Alt draws from the centre, and Space held mid drag
/// moves the whole box. Kept apart from the window so it can be tested without one.</summary>
internal sealed class AreaSelection(Size bounds)
{
    private readonly Rect screen = new(Point.Zero, bounds);
    private Point? anchor;
    private Point? current;
    private Point? last;
    private bool square;
    private bool fromCentre;

    public bool IsDragging => anchor is not null;

    public Rect? Selection
    {
        get
        {
            if (anchor is not { } from || current is not { } to) return null;
            var box = Rect.Dragged(from, to, square, fromCentre).Intersection(screen);
            return box.IsNull ? null : box;
        }
    }

    public void Down(Point point, bool square, bool fromCentre)
    {
        var at = Clamp(point);
        (anchor, current, last) = (at, at, at);
        (this.square, this.fromCentre) = (square, fromCentre);
    }

    public void Drag(Point point, bool square, bool fromCentre, bool spaceHeld)
    {
        if (anchor is not { } from) return;
        var at = Clamp(point);
        (this.square, this.fromCentre) = (square, fromCentre);
        if (spaceHeld && current is { } to && last is { } previous && Selection is { } box)
        {
            // Moved as a whole, and stopped at the monitor's edge rather than shrunk.
            var move = box.AllowedMove(new Vector(at.X - previous.X, at.Y - previous.Y), screen);
            (anchor, current, last) = (from.Offset(move), to.Offset(move), previous.Offset(move));
            return;
        }
        (current, last) = (at, at);
    }

    /// <summary>Arrow keys move the corner being dragged by one point.</summary>
    public void Nudge(double dx, double dy)
    {
        if (current is { } to) current = Clamp(new Point(to.X + dx, to.Y + dy));
    }

    /// <summary>The box let go of, or null for one under two points each way, which a click
    /// makes and nobody means to capture.</summary>
    public Rect? Up()
    {
        var box = Selection;
        (anchor, current, last) = (null, null, null);
        return box is { } rect && rect.Width >= 2 && rect.Height >= 2 ? rect : null;
    }

    /// <summary>The box's size in the capture's pixels, as the editor will open it.</summary>
    public string Readout(double scale)
    {
        if (Selection is not { } box) return "";
        var width = (int)Geometry.Round(box.Width * scale);
        var height = (int)Geometry.Round(box.Height * scale);
        return string.Create(CultureInfo.InvariantCulture, $"{width} × {height}");
    }

    /// <summary>Where the readout sits: below right of <paramref name="corner"/>, flipped back
    /// inside the monitor at its right and bottom edges.</summary>
    public Rect ReadoutBox(Point corner, Size text)
    {
        var box = new Rect(corner.X + 8, corner.Y + 8, text.Width + 12, text.Height + 6);
        var x = box.MaxX > screen.MaxX ? corner.X - box.Width - 8 : box.X;
        var y = box.MaxY > screen.MaxY ? corner.Y - box.Height - 8 : box.Y;
        return box with { X = x, Y = y };
    }

    private Point Clamp(Point point) =>
        new(Math.Min(Math.Max(point.X, 0), screen.Width), Math.Min(Math.Max(point.Y, 0), screen.Height));
}
