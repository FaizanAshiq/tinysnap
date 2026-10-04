namespace Tinysnap.Core;

/// <summary>Named for the Mac's keys. The Windows app maps Alt to <c>Option</c> and Ctrl to
/// <c>Command</c>.</summary>
[Flags]
public enum Modifiers
{
    None = 0,
    /// <summary>Squares boxes and snaps lines to 45 degrees.</summary>
    Shift = 1,
    /// <summary>Draws boxes out from where the drag began, as their centre.</summary>
    Option = 2,
    /// <summary>Held while drawing: moves the shape being drawn instead of resizing it.</summary>
    Space = 4,
    /// <summary>Held with any tool: picks up what is already applied, the way Photoshop's
    /// Command gives the move tool for as long as it is held.</summary>
    Command = 8,
}

public enum EscapeResult { FinishedTyping, Deselected, Close }

public enum GuideAxis { Vertical, Horizontal }

/// <summary>A line a dragged shape lines up on, in capture pixels: an edge or middle it shares with
/// another shape or the output, drawn from one across to the other while the drag lasts.
/// <paramref name="Position"/> is x for a vertical line and y for a horizontal one;
/// <paramref name="From"/> and <paramref name="To"/> run along the line.</summary>
public readonly record struct Guide(GuideAxis Axis, double Position, double From, double To);

/// <summary>Where Bring to Front, Bring Forward, Send Backward and Send to Back put a shape.</summary>
public enum Arrangement { Front, Forward, Backward, Back }

/// <summary>What the pointer is doing to the document, if anything.</summary>
public abstract record EditorPhase
{
    private EditorPhase() { }

    public static readonly EditorPhase Idle = new IdlePhase();

    public sealed record IdlePhase : EditorPhase;
    public sealed record Drawing(Guid Id, Point Anchor, Point Last) : EditorPhase;
    /// <summary><paramref name="Snap"/> is how far the shape has been nudged to line up, kept so
    /// the next step of the drag starts from where the pointer alone would have put it.</summary>
    public sealed record Moving(Guid Id, Point Last, Vector Snap = default) : EditorPhase;
    public sealed record Resizing(Annotation Original, Handle Handle) : EditorPhase;
    public sealed record Cropping(Rect Original, Handle Handle, Point Last) : EditorPhase;
    public sealed record Typing(Guid Id) : EditorPhase;
}

/// <summary>Everything the editor does with the pointer and the keyboard, with no UI
/// framework in it. The canvas view turns events into these calls and draws <c>Display</c>.</summary>
public sealed class EditorSession
{
    private readonly Dictionary<Tool, Style> styles;

    public EditHistory History { get; }

    /// <summary>What the canvas draws: the committed document, or the one a gesture is changing.</summary>
    public Document Display { get; private set; }

    public Tool Tool { get; private set; }
    public Guid? Selection { get; private set; }
    public EditorPhase Phase { get; private set; } = EditorPhase.Idle;

    /// <summary>The lines the shape being dragged lines up on, empty when it lines up on nothing.</summary>
    public IReadOnlyList<Guide> Guides { get; private set; } = [];

    /// <summary>The last style used with each tool, so each one remembers its own.</summary>
    public IReadOnlyDictionary<Tool, Style> Styles => styles;

    /// <summary>One colour for every tool: the last one picked, with whichever tool. Sizes and
    /// box shapes stay each tool's own.</summary>
    public string ColorHex { get; private set; }

    public EditorSession(Document document, Tool tool = Tool.Arrow, IReadOnlyDictionary<Tool, Style>? styles = null,
                         string colorHex = Palette.Red)
    {
        History = new EditHistory(document);
        Display = document;
        Tool = tool;
        this.styles = styles is null ? [] : new Dictionary<Tool, Style>(styles);
        ColorHex = colorHex;
    }

    public double Scale => Display.Scale;
    public bool IsUnsaved => History.IsUnsaved;

    public Annotation? SelectedAnnotation => Selection is { } id ? Display.Annotation(id) : null;

    /// <summary>The annotation whose text is being typed, which the canvas hides while its
    /// text field is showing.</summary>
    public Guid? TypingId => Phase is EditorPhase.Typing typing ? typing.Id : null;

    private bool IsIdle => Phase is EditorPhase.IdlePhase;

    public Style StyleFor(Tool tool) =>
        (styles.TryGetValue(tool, out var style) ? style : tool.DefaultStyle()) with { ColorHex = ColorHex };

    // Tools and styles

    /// <summary>Picking another tool lets go of the selection, so the next drag draws.</summary>
    public void Choose(Tool tool)
    {
        FinishTyping();
        if (tool != Tool || tool == Tool.Crop) Selection = null;
        Tool = tool;
    }

    /// <summary>What is under <paramref name="point"/>, for the hover border that shows what
    /// is applied where, with any tool: the annotation itself, or its border, which a filled
    /// box does not cover. Nothing while the crop tool is out, which only moves the crop.</summary>
    public Guid? Hovered(Point point, double reach = 0)
    {
        if (!IsIdle || Tool == Tool.Crop) return null;
        return Display.Topmost(point) ?? Display.BorderHit(point, reach);
    }

    /// <summary>Changes part of the style: the selection's, or the next annotation's when
    /// nothing is selected. Either way the tool remembers it.
    ///
    /// Only the part <paramref name="change"/> touches moves. Applying a whole style copied
    /// when the popover opened turned a large filled box into a small outline when only its
    /// colour was picked. <paramref name="merging"/> is for the colour panel, whose stream of
    /// changes to one annotation undoes as one step.</summary>
    public void Restyle(Func<Style, Style> change, bool merging = false)
    {
        // A locked shape keeps its style, and the tool keeps what it had.
        if (SelectedAnnotation?.IsLocked == true) return;
        if (Selection is not { } id || Display.Annotation(id) is not { } annotation)
        {
            var ratio = StyleFor(Tool).CropRatio;
            var style = change(StyleFor(Tool));
            styles[Tool] = style;
            ColorHex = style.ColorHex;
            // A picked ratio trims the crop to it, or the whole capture when nothing is cropped
            // yet, so the frame takes the ratio at once.
            if (Tool == Tool.Crop && style.CropRatio != ratio && style.CropRatio.Value() is { } value
                && Display.OutputRect.Trimmed(value).WholePixels is { Width: >= 1, Height: >= 1 } trimmed
                && trimmed != Display.OutputRect)
            {
                Display = Display with { Crop = trimmed };
                History.Commit(Display);
            }
            return;
        }
        var before = annotation.Style.ColorHex;
        annotation = annotation with { Style = change(annotation.Style) };
        styles[annotation.Tool] = annotation.Style;
        // Only a colour that was picked becomes the shared one. Stepping an old red
        // annotation's size leaves a blue shared colour alone.
        if (annotation.Style.ColorHex != before) ColorHex = annotation.Style.ColorHex;
        Display = Display.Replacing(annotation);
        // Every spotlight lights one shared area, so all of them dim or all of them blur.
        if (annotation.Kind is AnnotationKind.Spotlight)
        {
            var blur = annotation.Style.BlurOutside;
            Display = Display with
            {
                Annotations = [.. Display.Annotations.Select(a => a.Kind is AnnotationKind.Spotlight && a.Style.BlurOutside != blur
                    ? a with { Style = a.Style with { BlurOutside = blur } }
                    : a)],
            };
        }
        // A text still being typed is committed when typing ends, as one step.
        if (TypingId is null) History.Commit(Display, merging ? $"style {id}" : null);
    }

    // Measure

    /// <summary>The Measure tool's live reading, kept: one measurement a line, in one undo
    /// step, and none of them selected, so the next click measures again.</summary>
    public void Keep(IReadOnlyList<MeasureLine> lines)
    {
        if (lines.Count == 0) return;
        var style = StyleFor(Tool.Measure);
        var width = Tool.Measure.Points(style.Size) ?? 2;
        var kept = MeasureShape.ClearTags(lines, width, Display.Scale)
            .Select(line => Annotation.New(new AnnotationKind.Measure(line.From, line.To), style, line.LabelAt));
        Display = Display with { Annotations = Display.Annotations.AddRange(kept) };
        History.Commit(Display);
        Selection = null;
    }

    // Backdrop

    /// <summary>Sets or clears the backdrop as one undoable step. <paramref name="merging"/>
    /// is for the colour panel's stream of changes, which undoes as one.</summary>
    public void SetBackdrop(Backdrop? backdrop, bool merging = false)
    {
        Display = Display with { Backdrop = backdrop };
        History.Commit(Display, merging ? "backdrop" : null);
    }

    // Size

    /// <summary>Sets the export size as one undoable step, held to the limits. Null follows
    /// the Export setting again.</summary>
    public void SetResize(double? resize)
    {
        Display = Display with { Resize = resize is { } value ? Display.ClampedResize(value) : null };
        History.Commit(Display);
    }

    // Redact

    /// <summary>An erase box over each of <paramref name="boxes"/>, a little past each so no edge of a
    /// letter shows, all as one undoable step. Each stays a box of its own, to delete if it covers
    /// too much.</summary>
    public void Redact(IReadOnlyList<Rect> boxes)
    {
        if (boxes.Count == 0) return;
        FinishTyping();
        var margin = 2 * Scale;
        var style = StyleFor(Tool.Erase);
        Display = Display with
        {
            Annotations = Display.Annotations.AddRange(boxes.Select(box =>
                Annotation.New(new AnnotationKind.Erase(box.Inset(-margin, -margin).WholePixels), style))),
        };
        Selection = null;
        History.Commit(Display);
    }

    // Steps

    /// <summary>Sets where the steps start counting, as one undoable step, held to the limits.</summary>
    public void SetStepStart(int start)
    {
        Display = Display with { StepStart = start };
        History.Commit(Display);
    }

    // Magnifier

    /// <summary>The topmost magnifier under <paramref name="point"/> the scroll wheel may zoom: not
    /// a locked or hidden one.</summary>
    public Guid? Magnifier(Point point) =>
        Display.Annotations.LastOrDefault(a => a.Kind is AnnotationKind.Magnifier && !a.IsLocked && !a.IsHidden
                                               && a.Contains(point, Scale))?.Id;

    /// <summary>Zooms a magnifier half a step per scroll step, from 1.5x to 4x. A run of
    /// scroll steps on one lens undoes as one step.</summary>
    public void ZoomMagnifier(Guid id, int steps)
    {
        if (!IsIdle || Display.Annotation(id) is not { Kind: AnnotationKind.Magnifier(var center, var radius, var zoom) } lens
            || lens.IsLocked || lens.IsHidden)
            return;
        var zoomed = Math.Min(Math.Max(zoom + 0.5 * steps, 1.5), 4);
        Display = Display.Replacing(lens with { Kind = new AnnotationKind.Magnifier(center, radius, zoomed) });
        History.Commit(Display, $"zoom {id}");
    }

    // Pointer

    /// <summary><paramref name="reach"/> is how close, in capture pixels, a click must land to
    /// grab a handle.</summary>
    public void PointerDown(Point point, Modifiers modifiers = Modifiers.None, int clickCount = 1, double reach = 0)
    {
        // Clicking away ends typing, and that click does nothing else.
        if (TypingId is not null)
        {
            FinishTyping();
            return;
        }

        if (Tool == Tool.Crop)
        {
            var rect = Display.OutputRect;
            Phase = HandleNear(point, rect.HandlePoints, reach) is { } cropHandle
                ? new EditorPhase.Cropping(rect, cropHandle, point)
                : new EditorPhase.Cropping(new Rect(point, Size.Zero), Handle.BottomRight, point);
            return;
        }

        if (SelectedAnnotation is { IsLocked: false, IsHidden: false } selected
            && HandleNear(point, selected.Handles(Scale), reach) is { } handle)
        {
            Phase = new EditorPhase.Resizing(selected, handle);
            return;
        }

        if (clickCount >= 2 && Display.Topmost(point) is { } doubleClicked
            && Display.Annotation(doubleClicked) is { Kind: AnnotationKind.Text, IsLocked: false })
        {
            Selection = doubleClicked;
            Phase = new EditorPhase.Typing(doubleClicked);
            return;
        }

        // The select tool, or Command held with any other, picks up what is applied. A locked
        // shape is selected, so it can be unlocked, but stays where it is.
        if (Tool is Tool.Select or Tool.Image || modifiers.HasFlag(Modifiers.Command))
        {
            Selection = Display.Topmost(point);
            Phase = SelectedAnnotation is { IsLocked: false } picked ? new EditorPhase.Moving(picked.Id, point) : EditorPhase.Idle;
            return;
        }

        // With the text tool, clicking existing text edits it rather than starting a new one
        // on top. Locked text is drawn over instead.
        if (Tool == Tool.Text && Display.Topmost(point) is { } text
            && Display.Annotation(text) is { Kind: AnnotationKind.Text, IsLocked: false })
        {
            Selection = text;
            Phase = new EditorPhase.Typing(text);
            return;
        }

        // The selection is picked up anywhere on it, so a box just drawn moves at once.
        if (SelectedAnnotation is { IsLocked: false, IsHidden: false } chosen && chosen.Contains(point, Scale))
        {
            Phase = new EditorPhase.Moving(chosen.Id, point);
            return;
        }

        // A click on an annotation, or on its hover border, picks it up with any tool,
        // Command or not. The empty middle of an outline, or of a spotlight, still draws.
        if (Display.PickUp(point, reach) is { } under)
        {
            Selection = under;
            Phase = new EditorPhase.Moving(under, point);
            return;
        }

        StartDrawing(point);
    }

    /// <summary>Held keys work the way they do in Photoshop while a shape is drawn: Shift
    /// constrains, Option draws a box from its centre, and Space moves the whole shape, after
    /// which the drag carries on resizing from the new place.</summary>
    public void PointerDragged(Point point, Modifiers modifiers = Modifiers.None)
    {
        var constrained = modifiers.HasFlag(Modifiers.Shift);
        Vector Delta(Point last) => new(point.X - last.X, point.Y - last.Y);
        switch (Phase)
        {
            case EditorPhase.Drawing(var id, var anchor, var last):
            {
                if (Display.Annotation(id) is not { } annotation) return;
                if (modifiers.HasFlag(Modifiers.Space))
                {
                    var move = Delta(last);
                    Display = Display.Replacing(annotation.Moved(move));
                    Phase = new EditorPhase.Drawing(id, anchor.Offset(move), point);
                    return;
                }
                var kind = Drawn(annotation.Kind, anchor, point, constrained, modifiers.HasFlag(Modifiers.Option));
                Display = Display.Replacing(annotation with { Kind = kind });
                Phase = new EditorPhase.Drawing(id, anchor, point);
                break;
            }
            case EditorPhase.Moving(var id, var last, var snap):
            {
                if (Display.Annotation(id) is not { } annotation) return;
                // Moved from where the pointer alone would have it, so a snap lets go as soon as the
                // pointer carries the shape past it. Ctrl drags freely.
                var step = Delta(last);
                var free = annotation.Moved(new Vector(step.Dx - snap.Dx, step.Dy - snap.Dy));
                var lined = modifiers.HasFlag(Modifiers.Command) ? (Offset: default(Vector), Guides: []) : LiningUp(free);
                Display = Display.Replacing(free.Moved(lined.Offset));
                Guides = lined.Guides;
                Phase = new EditorPhase.Moving(id, point, lined.Offset);
                break;
            }
            case EditorPhase.Resizing(var original, var handle):
                Display = Display.Replacing(original.Resized(handle, point, constrained));
                break;
            case EditorPhase.Cropping(var original, var handle, var last):
            {
                // The whole canvas, grown part included, can be cropped.
                var bounds = Display.Extent;
                if (modifiers.HasFlag(Modifiers.Space))
                {
                    // Moved as a whole, and stopped at the capture's edge rather than shrunk.
                    var output = Display.OutputRect;
                    var move = output.AllowedMove(Delta(last), bounds);
                    Display = Display with { Crop = output.Offset(move.Dx, move.Dy) };
                    Phase = new EditorPhase.Cropping(original.Offset(move.Dx, move.Dy), handle, last.Offset(move));
                    return;
                }
                // A new crop, drawn from nothing, follows the same keys as a box. Dragging an
                // existing crop's handle only takes Shift. Either keeps a chosen ratio; Shift
                // squares it whatever the ratio.
                var isNew = original.Size == Size.Zero;
                var ratio = constrained ? 1 : StyleFor(Tool.Crop).CropRatio.Value();
                var rect = (isNew
                        ? Rect.Dragged(original.Origin, point, ratio, modifiers.HasFlag(Modifiers.Option))
                        : original.Resized(handle, point, ratio))
                    .Intersection(bounds).WholePixels;
                if (rect.Width >= 1 && rect.Height >= 1) Display = Display with { Crop = rect };
                Phase = new EditorPhase.Cropping(original, handle, point);
                break;
            }
        }
    }

    public void PointerUp()
    {
        switch (Phase)
        {
            case EditorPhase.Drawing(var id, _, _):
                Phase = EditorPhase.Idle;
                if (Display.Annotation(id) is not { } annotation) return;
                if (annotation.IsDegenerate(Scale))
                {
                    Display = Display.Removing(id);
                    Selection = null;
                    return;
                }
                History.Commit(Display);
                Selection = id;
                break;
            case EditorPhase.Moving or EditorPhase.Resizing or EditorPhase.Cropping:
                Phase = EditorPhase.Idle;
                Guides = [];
                History.Commit(Display);
                break;
        }
    }

    /// <summary>How far to nudge <paramref name="moving"/> so an edge or its middle lies on another
    /// shown shape's, or the output's, within five points on each axis; and the lines that show
    /// what it lines up on.</summary>
    private (Vector Offset, IReadOnlyList<Guide> Guides) LiningUp(Annotation moving)
    {
        var box = moving.Bounds(Scale);
        var reach = 5 * Scale;
        var others = Display.Annotations.Where(a => a.Id != moving.Id && !a.IsHidden).Select(a => a.Bounds(Scale))
            .Append(Display.OutputRect).ToList();
        double? Nudge(double[] mine, IEnumerable<double> theirs)
        {
            double? best = null;
            foreach (var a in mine)
                foreach (var b in theirs)
                    if (Math.Abs(b - a) <= reach && Math.Abs(b - a) < Math.Abs(best ?? double.PositiveInfinity)) best = b - a;
            return best;
        }
        var dx = Nudge([box.MinX, box.MidX, box.MaxX], others.SelectMany(o => new[] { o.MinX, o.MidX, o.MaxX }));
        var dy = Nudge([box.MinY, box.MidY, box.MaxY], others.SelectMany(o => new[] { o.MinY, o.MidY, o.MaxY }));
        var lined = box.Offset(dx ?? 0, dy ?? 0);
        var guides = new List<Guide>();
        if (dx is not null)
            foreach (var x in new[] { lined.MinX, lined.MidX, lined.MaxX })
                foreach (var other in others.Where(o => new[] { o.MinX, o.MidX, o.MaxX }.Any(v => Math.Abs(v - x) < 0.5)))
                    guides.Add(new Guide(GuideAxis.Vertical, x, Math.Min(lined.MinY, other.MinY), Math.Max(lined.MaxY, other.MaxY)));
        if (dy is not null)
            foreach (var y in new[] { lined.MinY, lined.MidY, lined.MaxY })
                foreach (var other in others.Where(o => new[] { o.MinY, o.MidY, o.MaxY }.Any(v => Math.Abs(v - y) < 0.5)))
                    guides.Add(new Guide(GuideAxis.Horizontal, y, Math.Min(lined.MinX, other.MinX), Math.Max(lined.MaxX, other.MaxX)));
        return (new Vector(dx ?? 0, dy ?? 0), guides);
    }

    private void StartDrawing(Point point)
    {
        var zero = new Rect(point, Size.Zero);
        AnnotationKind kind;
        switch (Tool)
        {
            case Tool.Arrow: kind = new AnnotationKind.Arrow(point, point); break;
            case Tool.Line: kind = new AnnotationKind.Line(point, point); break;
            case Tool.Highlighter:
                kind = StyleFor(Tool.Highlighter).Freehand
                    ? new AnnotationKind.HighlighterPath([point])
                    : new AnnotationKind.Highlighter(point, point);
                break;
            case Tool.Rectangle: kind = new AnnotationKind.Rectangle(zero); break;
            case Tool.Oval: kind = new AnnotationKind.Oval(zero); break;
            case Tool.Spotlight: kind = new AnnotationKind.Spotlight(zero); break;
            case Tool.Blur: kind = new AnnotationKind.Blur(zero); break;
            case Tool.Pixelate: kind = new AnnotationKind.Pixelate(zero); break;
            case Tool.Erase: kind = new AnnotationKind.Erase(zero); break;
            case Tool.Freehand: kind = new AnnotationKind.Freehand([point]); break;
            case Tool.Step: kind = new AnnotationKind.Step(point); break;
            case Tool.Magnifier: kind = new AnnotationKind.Magnifier(point, ToolInfo.MagnifierRadiusPoints * Scale, 2); break;
            case Tool.Text: kind = new AnnotationKind.Text(point, ""); break;
            case Tool.Measure:
                // Nothing to drag out. A click that picks nothing up lets go of the selection,
                // and the canvas keeps its live reading.
                Selection = null;
                return;
            default:
                // Select, image and crop draw nothing.
                return;
        }

        var annotation = Annotation.New(kind, StyleFor(Tool));
        Display = Display with { Annotations = Display.Annotations.Add(annotation) };
        Selection = annotation.Id;
        Phase = Tool == Tool.Text
            ? new EditorPhase.Typing(annotation.Id)
            : new EditorPhase.Drawing(annotation.Id, point, point);
    }

    /// <summary><paramref name="fromCentre"/> applies to boxes only: Photoshop's line tool
    /// does not centre either.</summary>
    private static AnnotationKind Drawn(AnnotationKind kind, Point anchor, Point point, bool constrained, bool fromCentre)
    {
        var end = constrained ? point.Snapped45(anchor) : point;
        var box = Rect.Dragged(anchor, point, constrained, fromCentre);
        return kind switch
        {
            AnnotationKind.Arrow => new AnnotationKind.Arrow(anchor, end),
            AnnotationKind.Line => new AnnotationKind.Line(anchor, end),
            AnnotationKind.Highlighter => new AnnotationKind.Highlighter(anchor, end),
            AnnotationKind.Measure => new AnnotationKind.Measure(anchor, end),
            AnnotationKind.Rectangle => new AnnotationKind.Rectangle(box),
            AnnotationKind.Oval => new AnnotationKind.Oval(box),
            AnnotationKind.Spotlight => new AnnotationKind.Spotlight(box),
            AnnotationKind.Blur => new AnnotationKind.Blur(box),
            AnnotationKind.Pixelate => new AnnotationKind.Pixelate(box),
            AnnotationKind.Erase => new AnnotationKind.Erase(box),
            AnnotationKind.Freehand(var points) => points.IsEmpty || points[^1].Distance(point) < 1
                ? kind
                : new AnnotationKind.Freehand(points.Add(point)),
            AnnotationKind.HighlighterPath(var points) => points.IsEmpty || points[^1].Distance(point) < 1
                ? kind
                : new AnnotationKind.HighlighterPath(points.Add(point)),
            AnnotationKind.Step => new AnnotationKind.Step(point),
            AnnotationKind.Magnifier(_, var radius, var zoom) => new AnnotationKind.Magnifier(point, radius, zoom),
            _ => kind,
        };
    }

    private static Handle? HandleNear(Point point, IReadOnlyList<(Handle Handle, Point Point)> handles, double reach) =>
        handles
            .Select(h => (h.Handle, Distance: h.Point.Distance(point)))
            .Where(h => h.Distance <= reach)
            .OrderBy(h => h.Distance)
            .Select(h => (Handle?)h.Handle)
            .FirstOrDefault();

    // Text

    public void UpdateTyping(string text)
    {
        if (TypingId is not { } id || Display.Annotation(id) is not { Kind: AnnotationKind.Text(var origin, _) } annotation)
            return;
        Display = Display.Replacing(annotation with { Kind = new AnnotationKind.Text(origin, text) });
    }

    /// <summary>Ends typing. Text left empty is removed: a new one leaves no trace in undo, an
    /// existing one emptied out is an ordinary, undoable delete.</summary>
    public void FinishTyping()
    {
        if (TypingId is not { } id) return;
        Phase = EditorPhase.Idle;
        if (Display.Annotation(id)?.IsDegenerate(Scale) == true)
        {
            Display = Display.Removing(id);
            Selection = null;
        }
        History.Commit(Display);
    }

    // Keyboard

    public void DeleteSelection()
    {
        if (!IsIdle || Selection is not { } id || SelectedAnnotation?.IsLocked == true) return;
        Display = Display.Removing(id);
        Selection = null;
        History.Commit(Display);
    }

    public void Nudge(double dx, double dy)
    {
        if (!IsIdle || SelectedAnnotation is not { IsLocked: false, IsHidden: false } annotation) return;
        Display = Display.Replacing(annotation.Moved(new Vector(dx, dy)));
        History.Commit(Display);
    }

    public EscapeResult Escape()
    {
        if (TypingId is not null)
        {
            FinishTyping();
            return EscapeResult.FinishedTyping;
        }
        if (Selection is not null)
        {
            Selection = null;
            return EscapeResult.Deselected;
        }
        return EscapeResult.Close;
    }

    public void Undo()
    {
        if (!IsIdle) return;
        History.Undo();
        Display = History.Document;
        if (Selection is { } id && Display.Annotation(id) is null) Selection = null;
    }

    public void Redo()
    {
        if (!IsIdle) return;
        History.Redo();
        Display = History.Document;
        if (Selection is { } id && Display.Annotation(id) is null) Selection = null;
    }

    // Layers

    /// <summary>Points a duplicate sits right of and below its original.</summary>
    public const double DuplicateOffset = 12;

    /// <summary>Selects a shape from the layers panel, whatever the tool, or nothing.</summary>
    public void Select(Guid? id)
    {
        FinishTyping();
        if (!IsIdle) return;
        Selection = id is { } chosen ? Display.Annotation(chosen)?.Id : null;
    }

    /// <summary>Moves the selection in the order. Front and back are the ends of the list.</summary>
    public void Arrange(Arrangement arrangement)
    {
        FinishTyping();
        if (Selection is not { } id) return;
        var index = Display.Annotations.FindIndex(a => a.Id == id);
        if (index < 0) return;
        var last = Display.Annotations.Length - 1;
        MoveLayer(id, arrangement switch
        {
            Arrangement.Front => last,
            Arrangement.Forward => Math.Min(index + 1, last),
            Arrangement.Backward => Math.Max(index - 1, 0),
            _ => 0,
        });
    }

    /// <summary>Puts a shape at <paramref name="index"/> in the list, bottom first, in one go, as
    /// Arrange does. One undo step, and none when it lands where it was.</summary>
    public void MoveLayer(Guid id, int index)
    {
        DragLayer(id, index);
        DropLayer();
    }

    /// <summary>A row being dragged in the layers list: its shape moves as the pointer goes, kept
    /// by <see cref="DropLayer"/> as one step, or put back by <see cref="CancelLayerDrag"/>.</summary>
    public void DragLayer(Guid id, int index)
    {
        FinishTyping();
        var from = Display.Annotations.FindIndex(a => a.Id == id);
        if (!IsIdle || from < 0) return;
        var target = Math.Clamp(index, 0, Display.Annotations.Length - 1);
        if (target == from) return;
        var moving = Display.Annotations[from];
        Display = Display with { Annotations = Display.Annotations.RemoveAt(from).Insert(target, moving) };
    }

    public void DropLayer()
    {
        if (IsIdle) History.Commit(Display);
    }

    public void CancelLayerDrag() => Display = History.Document;

    public void SetLocked(Guid id, bool locked) => Edit(id, a => a with { IsLocked = locked });

    public void SetHidden(Guid id, bool hidden) => Edit(id, a => a with { IsHidden = hidden });

    public void ToggleLock()
    {
        if (SelectedAnnotation is { } annotation) SetLocked(annotation.Id, !annotation.IsLocked);
    }

    /// <summary>A copy right above the original, a little right and down, unlocked and shown, so
    /// it can be moved at once. It becomes the selection.</summary>
    public void DuplicateSelection()
    {
        FinishTyping();
        if (!IsIdle || SelectedAnnotation is not { } original) return;
        var index = Display.Annotations.FindIndex(a => a.Id == original.Id);
        var offset = DuplicateOffset * Scale;
        var moved = original.Moved(new Vector(offset, offset));
        var copy = Annotation.New(moved.Kind, moved.Style, moved.LabelAt);
        Display = Display with { Annotations = Display.Annotations.Insert(index + 1, copy) };
        Selection = copy.Id;
        History.Commit(Display);
    }

    private void Edit(Guid id, Func<Annotation, Annotation> change)
    {
        FinishTyping();
        if (!IsIdle || Display.Annotation(id) is not { } annotation) return;
        Display = Display.Replacing(change(annotation));
        History.Commit(Display);
    }

    // Images

    /// <summary>Adds a pasted or dropped image, centred at its own point size and scaled down
    /// to fit the capture if it is larger. It is then selected, and the tool in hand stays: a
    /// selection moves under any tool.</summary>
    public void Insert(PastedImage image, Size pointSize)
    {
        if (pointSize.Width <= 0 || pointSize.Height <= 0) return;
        FinishTyping();
        var bounds = Display.Capture.Bounds;
        var natural = new Size(pointSize.Width * Scale, pointSize.Height * Scale);
        var fit = Math.Min(1, Math.Min(bounds.Width / natural.Width, bounds.Height / natural.Height));
        var size = new Size(natural.Width * fit, natural.Height * fit);
        var rect = new Rect(bounds.MidX - size.Width / 2, bounds.MidY - size.Height / 2, size.Width, size.Height);

        // Corners carry over from the last image; see-through and difference do not, so a new
        // paste is never a faint or inverted surprise.
        var style = StyleFor(Tool.Image) with { Opacity = 1, Difference = false };
        var annotation = Annotation.New(new AnnotationKind.Image(rect, image), style);
        Display = Display with { Annotations = Display.Annotations.Add(annotation) };
        History.Commit(Display);
        Selection = annotation.Id;
    }

    public void MarkSaved() => History.MarkSaved();
}
