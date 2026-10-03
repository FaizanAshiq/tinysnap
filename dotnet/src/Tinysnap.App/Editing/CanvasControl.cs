using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SkiaSharp;
using Tinysnap.Core;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaRect = Avalonia.Rect;
using AvaloniaSize = Avalonia.Size;
using Point = Tinysnap.Core.Point;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.App.Editing;

/// <summary>Draws the document and turns pointer and key events into <see cref="EditorSession"/>
/// calls. The control is sized to the canvas at its zoom, so everything drawn over the capture
/// is in plain DIPs and stays the same size on screen at any zoom.</summary>
internal sealed partial class CanvasControl : Control
{
    private static readonly SKSamplingOptions Crisp = new(SKFilterMode.Nearest, SKMipmapMode.None);
    private static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private SharedImage? rendered;
    private Document? renderedDocument;
    private Guid? renderedHidden;
    private bool renderedFramed;
    /// <summary>The backdrop's fill and shadow, kept while the frame's size and backdrop hold.</summary>
    private FrameGround? ground;
    /// <summary>Past 100%, what is in view drawn at the screen's resolution, and what it was drawn for.</summary>
    private (SharedImage Image, Rect Region)? closeUp;
    private (Document Document, Guid? Hidden, Rect Visible, double OutputScale, bool Framed)? closeUpKey;
    private double zoom = 1;

    public CanvasControl(EditorSession session)
    {
        Session = session;
        Focusable = true;
        ClipToBounds = true;
        Shown = ShownRect();
        AcceptDrops();
    }

    public EditorSession Session { get; }

    public double Zoom
    {
        get => zoom;
        set
        {
            zoom = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Anything the toolbar or style bar shows may have changed.</summary>
    public event Action? Changed;

    /// <summary>Ctrl with the wheel, in notches: the editor zooms.</summary>
    public event Action<double>? ZoomRequested;

    /// <summary>The colour under the pointer, as "#RRGGBB", for the readout.</summary>
    public event Action<string>? PointerColor;

    /// <summary>What the canvas shows, in capture pixels: the framed output with a backdrop on,
    /// otherwise the whole extent, whose corner moves once a shape is drawn past the edge.</summary>
    internal Rect Shown { get; private set; }

    internal CanvasMapping Mapping => new(Shown, Session.Scale, zoom);

    /// <summary>A backdrop shows round the output, except while the crop tool is out, when the
    /// whole capture shows so the crop can be changed.</summary>
    private bool Framed => Session.Display.Backdrop is not null && Session.Tool != Tool.Crop;

    private Rect ShownRect() => (Framed ? Session.Display.FramedRect : null) ?? Session.Display.Extent;

    protected override AvaloniaSize MeasureOverride(AvaloniaSize availableSize)
    {
        textBox?.Measure(AvaloniaSize.Infinity);
        var size = Mapping.Size;
        return new AvaloniaSize(size.Width, size.Height);
    }

    protected override AvaloniaSize ArrangeOverride(AvaloniaSize finalSize)
    {
        textBox?.Arrange(new AvaloniaRect(TextOrigin, textBox.DesiredSize));
        return finalSize;
    }

    /// <summary>After every session call: the canvas may have grown, and the toolbar may need to
    /// follow.</summary>
    internal void SessionChanged()
    {
        var shown = ShownRect();
        if (shown != Shown)
        {
            // ponytail: the capture shifts under the pointer while a shape drawn past the left or
            // top edge grows the canvas; hold the scroll position mid gesture as the Mac does if
            // that feels wrong on Windows.
            Shown = shown;
            InvalidateMeasure();
        }
        SyncTextBox();
        InvalidateVisual();
        Changed?.Invoke();
    }

    // Drawing

    public override void Render(DrawingContext context)
    {
        var size = Mapping.Size;
        var bounds = new AvaloniaRect(0, 0, size.Width, size.Height);
        var perPixel = zoom * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) / Session.Scale;
        // Past one screen pixel per capture pixel, every pixel stays a sharp square.
        var close = perPixel > 1.001;
        if (Rendered() is { } image)
        {
            var held = image.Acquire();
            var target = new SKRect(0, 0, (float)size.Width, (float)size.Height);
            var sampling = close ? Crisp : Smooth;
            context.Custom(new SkiaDraw(bounds, canvas => canvas.DrawImage(held.Image, target, sampling), held.Release));
        }
        if (close) DrawCloseUp(context, bounds, perPixel);
        DrawCrop(context, bounds);
        DrawBorders(context);
        DrawSelection(context);
        DrawLiveReading(context, bounds);
        DrawTextPick(context, bounds);
    }

    /// <summary>The document rendered, again only when it, the text being typed or the framing
    /// changed. With a backdrop the canvas shows exactly what an export gives, drawn over the kept
    /// ground so only a new size or backdrop pays for the shadow.</summary>
    private SharedImage? Rendered()
    {
        var hidden = Session.TypingId;
        var framed = Framed;
        if (rendered is not null && renderedDocument == Session.Display && renderedHidden == hidden && renderedFramed == framed)
            return rendered;
        var hiding = hidden is { } id ? new HashSet<Guid> { id } : null;
        var image = framed
            ? Renderer.RenderFramed(Session.Display, 1, hiding, ref ground)
            : Renderer.Render(Session.Display, hidden: hiding);
        rendered?.Release();
        rendered = image is null ? null : new SharedImage(image);
        (renderedDocument, renderedHidden, renderedFramed) = (Session.Display, hidden, framed);
        return rendered;
    }

    /// <summary>Past 100%, what is in view drawn again at the screen's own resolution over the
    /// enlarged render, so shapes stay smooth however far in while the capture's pixels stay
    /// sharp squares. With a backdrop it is clipped to the output's rounded corners.</summary>
    private void DrawCloseUp(DrawingContext context, AvaloniaRect bounds, double outputScale)
    {
        var view = bounds;
        // Only the part the editor's scroll view shows; the whole canvas outside one.
        if (this.FindAncestorOfType<ScrollViewer>() is { } scroll && scroll.TranslatePoint(default, this) is { } corner)
            view = view.Intersect(new AvaloniaRect(corner, scroll.Viewport));
        var origin = Mapping.ToPixels(new Point(view.X, view.Y));
        var visible = new Rect(origin.X, origin.Y, view.Width / zoom * Session.Scale, view.Height / zoom * Session.Scale);
        var key = (Session.Display, Session.TypingId, visible, outputScale, Framed);
        if (closeUpKey != key)
        {
            closeUp?.Image.Release();
            var hiding = key.TypingId is { } id ? new HashSet<Guid> { id } : null;
            closeUp = Renderer.RenderCloseUp(key.Display, visible, outputScale, key.Framed, hiding) is { } made
                ? (new SharedImage(made.Image), made.Region)
                : null;
            closeUpKey = key;
        }
        if (closeUp is not { } shown) return;
        var held = shown.Image.Acquire();
        var target = ToDips(shown.Region);
        var output = ToDips(Session.Display.OutputPixelRect);
        var clip = Session.Display.Backdrop is { } backdrop && key.Framed
            ? Math.Min((backdrop.Corners.Points() ?? double.MaxValue) * zoom, Math.Min(output.Width, output.Height) / 2)
            : (double?)null;
        // ponytail: drawn over the enlarged render, so a half see-through pixel (a window
        // capture's corner) comes out a little more solid; clip the render out first if it shows.
        context.Custom(new SkiaDraw(bounds, canvas =>
        {
            canvas.Save();
            if (clip is { } radius)
                canvas.ClipRoundRect(new SKRoundRect(new SKRect((float)output.X, (float)output.Y, (float)output.Right, (float)output.Bottom),
                                                     (float)radius), antialias: true);
            canvas.DrawImage(held.Image, new SKRect((float)target.X, (float)target.Y, (float)target.Right, (float)target.Bottom), Crisp);
            canvas.Restore();
        }, held.Release));
    }

    private static IBrush AccentBrush => new SolidColorBrush(Accent.Color);

    private AvaloniaRect ToDips(Rect pixels)
    {
        var rect = Mapping.ToDips(pixels);
        return new AvaloniaRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private void DrawCrop(DrawingContext context, AvaloniaRect bounds)
    {
        // Framed, only the crop shows, so there is nothing outside it to dim.
        if (!(Session.Tool == Tool.Crop || Session.Display.Crop is not null && !Framed)) return;
        var crop = ToDips(Session.Display.OutputRect);
        var shade = new GeometryGroup
        {
            FillRule = FillRule.EvenOdd,
            Children = { new RectangleGeometry(bounds), new RectangleGeometry(crop) },
        };
        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), null, shade);
        if (Session.Tool == Tool.Crop) DrawHandles(context, Session.Display.OutputRect.HandlePoints.Select(h => h.Point));
    }

    /// <summary>Whatever is under the pointer gets a border, with any tool, so it is plain what
    /// is applied where; with Ctrl held, or the select tool out, everything does.</summary>
    private void DrawBorders(DrawingContext context)
    {
        var showAll = commandHeld || Session.Tool == Tool.Select;
        foreach (var annotation in Session.Display.Annotations)
        {
            if (annotation.Id == Session.Selection || annotation.IsHidden) continue;
            var isHovered = annotation.Id == Hovered;
            if (!showAll && !isHovered) continue;
            var box = ToDips(annotation.Bounds(Session.Scale)).Inflate(3);
            var pen = new Pen(AccentBrush, isHovered ? 1.5 : 1, isHovered ? null : new DashStyle([3, 3], 0));
            using (context.PushOpacity(isHovered ? 1 : 0.7)) context.DrawRectangle(null, pen, box);
        }
    }

    /// <summary>A hidden selection shows nothing; a locked one its outline and a lock, no
    /// handles.</summary>
    private void DrawSelection(DrawingContext context)
    {
        if (Session.SelectedAnnotation is not { IsHidden: false } annotation || Session.TypingId is not null) return;
        var outline = ToDips(annotation.Bounds(Session.Scale)).Inflate(2);
        context.DrawRectangle(null, new Pen(AccentBrush, 1, new DashStyle([4, 3], 0)), outline);
        if (annotation.IsLocked)
        {
            DrawLockBadge(context, outline.TopRight);
            return;
        }
        DrawHandles(context, annotation.Handles(Session.Scale).Select(h => h.Point));
    }

    private static readonly Avalonia.Media.Geometry LockGlyph = Avalonia.Media.Geometry.Parse(ToolIcons.Lock);

    /// <summary>An accent disc with a white lock, centred on the outline's top right corner.</summary>
    private void DrawLockBadge(DrawingContext context, AvaloniaPoint corner)
    {
        context.DrawEllipse(AccentBrush, null, corner, 9, 9);
        // The 20 point glyph at 12 points, centred on the disc.
        using (context.PushTransform(Matrix.CreateScale(0.6, 0.6) * Matrix.CreateTranslation(corner.X - 6, corner.Y - 6.5)))
            context.DrawGeometry(Brushes.White, null, LockGlyph);
    }

    /// <summary>The layers panel's row under the pointer borders its shape, as hovering it here does.</summary>
    internal void Highlight(Guid? id)
    {
        Hovered = id;
        InvalidateVisual();
    }

    /// <summary>For the layers panel and its shortcuts: a session change, then everything that
    /// follows one.</summary>
    internal void Apply(Action<EditorSession> change)
    {
        change(Session);
        SessionChanged();
    }

    private void DrawHandles(DrawingContext context, IEnumerable<Point> points)
    {
        var pen = new Pen(AccentBrush, 1);
        foreach (var point in points)
        {
            var at = Mapping.ToDips(point);
            context.DrawRectangle(Brushes.White, pen, new AvaloniaRect(at.X - 4, at.Y - 4, 8, 8));
        }
    }

    // Measuring

    /// <summary>Where the pointer rests, in capture pixels, while the Measure tool is out.</summary>
    private Point? measurePointer;

    /// <summary>Read once per capture: the walks need each pixel's brightness, not its colour.</summary>
    private (Capture Capture, LuminanceBuffer Buffer)? luminance;

    /// <summary>The Measure tool's lines, edge contrast and guide, as the editor last set them.</summary>
    public MeasureSettings MeasureSettings
    {
        get => measureSettings;
        set
        {
            if (measureSettings == value) return;
            measureSettings = value;
            InvalidateVisual();
            Changed?.Invoke();
        }
    }

    private MeasureSettings measureSettings = MeasureSettings.Defaults;

    /// <summary>A key or a chip changed the Measure settings here, for the app to remember and
    /// pass on to every other editor.</summary>
    public event Action<MeasureSettings>? MeasureChanged;

    public void ChangeMeasure(Func<MeasureSettings, MeasureSettings> change)
    {
        MeasureSettings = change(MeasureSettings);
        MeasureChanged?.Invoke(MeasureSettings);
    }

    /// <summary>With Measure in hand: X and Y toggle the lines, and the up and down arrows step the
    /// edge contrast, 5% with Shift, while nothing is selected for them to nudge.</summary>
    private bool MeasureKey(KeyEventArgs e)
    {
        if (Session.Tool != Tool.Measure || Session.Phase is not EditorPhase.IdlePhase) return false;
        if (e.Key is Key.Up or Key.Down && Session.Selection is null && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
        {
            ChangeMeasure(m => m.StepContrast(up: e.Key == Key.Up, coarse: e.KeyModifiers.HasFlag(KeyModifiers.Shift)));
            return true;
        }
        if (e.KeyModifiers != KeyModifiers.None) return false;
        switch (e.Key)
        {
            case Key.X:
                ChangeMeasure(m => m with { Across = !m.Across });
                return true;
            case Key.Y:
                ChangeMeasure(m => m with { Down = !m.Down });
                return true;
            default:
                return false;
        }
    }

    /// <summary>What a click would keep. Nothing over an annotation, where a click picks it up.</summary>
    private IReadOnlyList<MeasureLine> LiveReading()
    {
        if (Session.Tool != Tool.Measure || IsPickingText || Session.Phase is not EditorPhase.IdlePhase || overPickUp
            || measurePointer is not { } at)
            return [];
        var capture = Session.Display.Capture;
        if (luminance?.Capture != capture)
            luminance = LuminanceBuffer.From(capture.Image) is { } buffer ? (capture, buffer) : null;
        return luminance is { Buffer: var read } ? MeasureReading.Lines(at, read, Session.Scale, MeasureSettings) : [];
    }

    /// <summary>Drawn by the same code as a kept measurement and placed as keeping places it, so a
    /// click keeps what is on screen.</summary>
    private void DrawLiveReading(DrawingContext context, AvaloniaRect bounds)
    {
        var lines = LiveReading();
        if (lines.Count == 0) return;
        var style = Session.StyleFor(Tool.Measure);
        var width = Tool.Measure.Points(style.Size) ?? 2;
        var color = Palette.Color(style.ColorHex);
        var scale = Session.Scale;
        var placed = MeasureShape.ClearTags(lines, width, scale).ToArray();
        var (shown, factor) = (Shown, zoom / scale);
        context.Custom(new SkiaDraw(bounds, canvas =>
        {
            canvas.Save();
            canvas.Scale((float)factor);
            canvas.Translate((float)-shown.MinX, (float)-shown.MinY);
            foreach (var line in placed) MeasureShape.Draw(canvas, line.From, line.To, width, color, scale, line.LabelAt);
            canvas.Restore();
        }));
    }

    // Pointer

    /// <summary>Space is a key, not a modifier, so whether it is held is tracked here.</summary>
    private bool spaceHeld;
    private bool commandHeld;
    private Point? lastPoint;
    private double wheelCarry;

    /// <summary>Over something a click picks up with any tool.</summary>
    private bool overPickUp;

    internal Guid? Hovered { get; private set; }

    private bool IsDrawing => Session.Phase is EditorPhase.Drawing or EditorPhase.Cropping;

    private static bool IsCommand(KeyModifiers keys) => (keys & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

    /// <summary>Ctrl is the Mac's Command, and on the Mac development build Cmd is too.</summary>
    private Modifiers ModifiersOf(KeyModifiers keys)
    {
        var modifiers = Modifiers.None;
        if (keys.HasFlag(KeyModifiers.Shift)) modifiers |= Modifiers.Shift;
        if (keys.HasFlag(KeyModifiers.Alt)) modifiers |= Modifiers.Option;
        if (IsCommand(keys)) modifiers |= Modifiers.Command;
        if (spaceHeld) modifiers |= Modifiers.Space;
        return modifiers;
    }

    private Point PixelAt(PointerEventArgs e)
    {
        var at = e.GetPosition(this);
        return Mapping.ToPixels(new Point(at.X, at.Y));
    }

    private void Hover(Point point)
    {
        var reach = Mapping.Reach;
        Hovered = Session.Hovered(point, reach);
        overPickUp = Session.Phase is EditorPhase.IdlePhase && Session.Tool != Tool.Crop
            && Session.Display.PickUp(point, reach) is not null;
        // The open hand only over what can be moved: never over a locked shape.
        Cursor = (commandHeld || overPickUp || Session.Tool is Tool.Select or Tool.Image)
                 && Hovered is { } hovered && Session.Display.Annotation(hovered) is { IsLocked: false }
            ? new Cursor(StandardCursorType.Hand)
            : Cursor.Default;
    }

    private void ReportColor(Point point)
    {
        if (rendered is null) return;
        var held = rendered.Acquire();
        try
        {
            var hex = ColorProbe.Hex(held.Image, (int)Math.Floor(point.X - Shown.MinX), (int)Math.Floor(point.Y - Shown.MinY));
            if (hex is not null) PointerColor?.Invoke(hex);
        }
        finally { held.Release(); }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        var point = PixelAt(e);
        if (IsPickingText)
        {
            TextPickPressed(point);
            e.Handled = true;
            return;
        }
        lastPoint = point;
        commandHeld = IsCommand(e.KeyModifiers);
        var reading = LiveReading();
        Session.PointerDown(point, ModifiersOf(e.KeyModifiers), e.ClickCount, Mapping.Reach);
        // A Measure click that picked nothing up keeps what was showing.
        if (Session.Tool == Tool.Measure && Session.Phase is EditorPhase.IdlePhase && Session.Selection is null)
            Session.Keep(reading);
        Hover(point);
        e.Handled = true;
        SessionChanged();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = PixelAt(e);
        if (IsPickingText)
        {
            TextPickMoved(point);
            return;
        }
        commandHeld = IsCommand(e.KeyModifiers);
        if (Session.Phase is not EditorPhase.IdlePhase)
        {
            lastPoint = point;
            Session.PointerDragged(point, ModifiersOf(e.KeyModifiers));
            SessionChanged();
        }
        else
        {
            Hover(point);
            measurePointer = Session.Tool == Tool.Measure ? point : null;
            InvalidateVisual();
        }
        ReportColor(point);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (IsPickingText)
        {
            TextPickReleased();
            return;
        }
        lastPoint = null;
        Session.PointerUp();
        Hover(PixelAt(e));
        SessionChanged();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Hovered = null;
        measurePointer = null;
        InvalidateVisual();
    }

    /// <summary>Over a magnifier the wheel zooms it; with Ctrl it zooms the canvas; anywhere else
    /// it scrolls.</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (IsCommand(e.KeyModifiers))
        {
            ZoomRequested?.Invoke(e.Delta.Y);
            e.Handled = true;
            return;
        }
        if (Session.Phase is not EditorPhase.IdlePhase || Session.Magnifier(PixelAt(e)) is not { } lens)
        {
            wheelCarry = 0;
            base.OnPointerWheelChanged(e);
            return;
        }
        // A wheel sends whole notches and a touchpad small fractions, so both come out at half a
        // step of zoom per notch.
        wheelCarry += e.Delta.Y;
        var steps = (int)wheelCarry;
        e.Handled = true;
        if (steps == 0) return;
        wheelCarry -= steps;
        Session.ZoomMagnifier(lens, steps);
        SessionChanged();
    }

    // Keys that shape a gesture; the rest are in CanvasControl.Keys.cs.

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LWin or Key.RWin;

    /// <summary>Pressing or letting go of Shift or Alt mid drag reshapes at once, without waiting
    /// for the pointer to move. Ctrl shows every border while it is held.</summary>
    private bool ShapeGesture(KeyEventArgs e, bool down)
    {
        commandHeld = IsCommand(e.KeyModifiers);
        if (e.Key == Key.Space && (IsDrawing || !down))
        {
            spaceHeld = down && IsDrawing;
            return down;
        }
        if (!IsModifierKey(e.Key)) return false;
        InvalidateVisual();
        if (IsDrawing && lastPoint is { } point)
        {
            Session.PointerDragged(point, ModifiersOf(e.KeyModifiers));
            SessionChanged();
        }
        return false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (ShapeGesture(e, down: true) || HandleKey(e))
        {
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        ShapeGesture(e, down: false);
        base.OnKeyUp(e);
    }

    /// <summary>Editing keys, added with the text field in CanvasControl.Keys.cs.</summary>
    private partial bool HandleKey(KeyEventArgs e);
}
