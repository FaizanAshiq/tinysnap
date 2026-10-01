using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;
using Tinysnap.Platform;
using CorePoint = Tinysnap.Core.Point;
using CoreRect = Tinysnap.Core.Rect;
using CoreSize = Tinysnap.Core.Size;

namespace Tinysnap.App.Capturing;

/// <summary>One monitor's overlay: borderless, on top, placed at the monitor's pixel origin and
/// sized to it in points, so the frozen image lies exactly over what it froze. Its own
/// coordinates are the monitor's points from its top left corner.</summary>
internal sealed class OverlayWindow : Window
{
    private readonly AreaOverlay owner;
    private readonly AreaSelection selection;
    private Point? pointer;
    private bool spaceHeld;
    private PickableWindow? hovered;

    public FrozenScreen Screen { get; }

    public OverlayWindow(FrozenScreen screen, AreaOverlay owner)
    {
        Screen = screen;
        this.owner = owner;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint((int)screen.Bounds.X, (int)screen.Bounds.Y);
        Width = screen.Bounds.Width / screen.Scale;
        Height = screen.Bounds.Height / screen.Scale;
        Background = Brushes.Black;
        Cursor = new Cursor(StandardCursorType.Cross);
        selection = new AreaSelection(new CoreSize(Width, Height));
        Content = new Surface(this);
    }

    internal void WindowModeChanged()
    {
        hovered = owner.WindowMode && pointer is { } at ? owner.WindowAt(ToPixel(at)) : null;
        Redraw();
    }

    private CorePoint ToPixel(Point point) =>
        new(Screen.Bounds.X + point.X * Screen.Scale, Screen.Bounds.Y + point.Y * Screen.Scale);

    private Rect Local(CoreRect pixels) =>
        new((pixels.MinX - Screen.Bounds.X) / Screen.Scale, (pixels.MinY - Screen.Bounds.Y) / Screen.Scale,
            pixels.Width / Screen.Scale, pixels.Height / Screen.Scale);

    private void Redraw() => (Content as Control)?.InvalidateVisual();

    private static (bool Square, bool FromCentre) Keys(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Shift), modifiers.HasFlag(KeyModifiers.Alt));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var at = e.GetPosition(this);
        if (owner.WindowMode)
        {
            if (owner.WindowAt(ToPixel(at)) is { } picked) owner.Finish(new AreaResult.PickedWindow(picked));
            return;
        }
        var (square, fromCentre) = Keys(e.KeyModifiers);
        selection.Down(new CorePoint(at.X, at.Y), square, fromCentre);
        pointer = at;
        Redraw();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var at = e.GetPosition(this);
        pointer = at;
        if (owner.WindowMode) hovered = owner.WindowAt(ToPixel(at));
        else if (selection.IsDragging)
        {
            var (square, fromCentre) = Keys(e.KeyModifiers);
            selection.Drag(new CorePoint(at.X, at.Y), square, fromCentre, spaceHeld);
        }
        Redraw();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        pointer = null;
        Redraw();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (owner.WindowMode) return;
        Timing.Mark("pointer released");
        if (selection.Up() is { } box) owner.Finish(new AreaResult.Area(Screen, box));
        else Redraw();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                owner.Finish(new AreaResult.Cancelled());
                break;
            case Key.Space when selection.IsDragging:
                spaceHeld = true;
                break;
            case Key.Space:
                owner.ToggleWindowMode();
                break;
            case Key.Left: selection.Nudge(-1, 0); break;
            case Key.Right: selection.Nudge(1, 0); break;
            case Key.Up: selection.Nudge(0, -1); break;
            case Key.Down: selection.Nudge(0, 1); break;
            default:
                Reshape(e.KeyModifiers);
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
        Redraw();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space) spaceHeld = false;
        else Reshape(e.KeyModifiers);
        base.OnKeyUp(e);
    }

    /// <summary>Pressing or letting go of Shift or Alt mid drag reshapes the box at once,
    /// without waiting for the pointer to move.</summary>
    private void Reshape(KeyModifiers modifiers)
    {
        if (!selection.IsDragging || pointer is not { } at) return;
        var (square, fromCentre) = Keys(modifiers);
        selection.Drag(new CorePoint(at.X, at.Y), square, fromCentre, spaceHeld: false);
        Redraw();
    }

    /// <summary>The frozen image, the dimming, the box and its size, the crosshair, and in
    /// window mode the window under the pointer lit.</summary>
    private sealed class Surface(OverlayWindow window) : Control
    {
        private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(89, 0, 0, 0));
        private static readonly IBrush Lit = new SolidColorBrush(Color.FromArgb(64, 10, 132, 255));
        private static readonly IPen Edge = new Pen(Brushes.White, 1);
        private static readonly IPen Cross = new Pen(new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)), 1);
        private static readonly IBrush ReadoutGround = new SolidColorBrush(Color.FromArgb(191, 0, 0, 0));
        private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            var image = window.Screen.Image;
            var target = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);
            context.Custom(new SkiaDraw(bounds, canvas => { canvas.DrawImage(image, target, Sampling); Timing.Mark("overlay drawn"); }));

            var box = window.selection.Selection;
            Rect? lit = box is { } b ? new Rect(b.X, b.Y, b.Width, b.Height)
                : window.owner.WindowMode && window.hovered is { } picked ? window.Local(picked.Bounds) : null;

            var shade = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { new RectangleGeometry(bounds) } };
            if (lit is { } hole) shade.Children.Add(new RectangleGeometry(hole));
            context.DrawGeometry(Dim, null, shade);

            if (lit is { } shown)
            {
                if (window.owner.WindowMode) context.DrawRectangle(Lit, null, shown);
                context.DrawRectangle(null, Edge, shown.Deflate(0.5));
            }

            if (box is { } selected)
                DrawReadout(context, window.selection.Readout(window.Screen.Scale), new CorePoint(selected.MaxX, selected.MaxY));
            else if (!window.owner.WindowMode && window.pointer is { } at)
            {
                context.DrawLine(Cross, new Point(at.X, 0), new Point(at.X, bounds.Height));
                context.DrawLine(Cross, new Point(0, at.Y), new Point(bounds.Width, at.Y));
            }
        }

        private void DrawReadout(DrawingContext context, string text, CorePoint corner)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                              new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium), 12,
                                              Brushes.White);
            var box = window.selection.ReadoutBox(corner, new CoreSize(formatted.Width, formatted.Height));
            context.DrawRectangle(ReadoutGround, null, new Rect(box.X, box.Y, box.Width, box.Height), 5, 5);
            context.DrawText(formatted, new Point(box.X + 6, box.Y + 3));
        }
    }
}
