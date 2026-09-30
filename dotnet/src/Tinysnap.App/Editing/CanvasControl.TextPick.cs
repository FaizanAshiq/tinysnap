using Avalonia.Input;
using Avalonia.Media;
using AvaloniaRect = Avalonia.Rect;
using Point = Tinysnap.Core.Point;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.App.Editing;

/// <summary>Copy Text's side of the canvas: while it is on the capture dims, a drag draws a box,
/// and the box, in capture pixels, is handed on; a click hands on nothing, which reads the whole
/// capture. It stays on until Esc, another tool or Copy Text again, and the tool in hand stays
/// in hand for when it ends.</summary>
internal sealed partial class CanvasControl
{
    private Action<Rect?>? textPick;
    private Point? textPickStart;
    private Rect? textPickBox;

    public bool IsPickingText => textPick is not null;

    /// <summary>Copy Text ended, by Esc, another tool or Copy Text again.</summary>
    public event Action? TextPickStopped;

    /// <param name="onPick">Takes each box dragged, or null for a click.</param>
    public void PickText(Action<Rect?> onPick)
    {
        Session.FinishTyping();
        textPick = onPick;
        textPickStart = null;
        textPickBox = null;
        Cursor = new Cursor(StandardCursorType.Cross);
        Focus();
        SessionChanged();
    }

    public void StopPickingText()
    {
        if (textPick is null) return;
        textPick = null;
        textPickStart = null;
        textPickBox = null;
        Cursor = Cursor.Default;
        InvalidateVisual();
        TextPickStopped?.Invoke();
    }

    private void TextPickPressed(Point point)
    {
        textPickStart = point;
        textPickBox = null;
    }

    private void TextPickMoved(Point point)
    {
        if (textPickStart is not { } start) return;
        textPickBox = new Rect(Math.Min(start.X, point.X), Math.Min(start.Y, point.Y), Math.Abs(point.X - start.X), Math.Abs(point.Y - start.Y));
        InvalidateVisual();
    }

    private void TextPickReleased()
    {
        if (textPick is not { } pick) return;
        var box = textPickBox;
        textPickStart = null;
        textPickBox = null;
        InvalidateVisual();
        // A box too small to hold a word is a click, which reads the whole capture.
        pick(box is { } dragged && dragged.Width >= 4 * Session.Scale && dragged.Height >= 4 * Session.Scale ? dragged : null);
    }

    /// <summary>Everything dimmed but the box being dragged.</summary>
    private void DrawTextPick(DrawingContext context, AvaloniaRect bounds)
    {
        if (textPick is null) return;
        Geometry shade = new RectangleGeometry(bounds);
        if (textPickBox is { } box)
        {
            var hole = ToDips(box);
            shade = new CombinedGeometry(GeometryCombineMode.Exclude, shade, new RectangleGeometry(hole));
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(89, 0, 0, 0)), null, shade);
            context.DrawRectangle(null, new Pen(AccentBrush, 1.5), hole);
            return;
        }
        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(89, 0, 0, 0)), null, shade);
    }
}
