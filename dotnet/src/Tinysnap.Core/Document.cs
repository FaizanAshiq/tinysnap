using System.Collections.Immutable;
using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>The pixels a capture produced. A class so undo snapshots share one copy.</summary>
public sealed class Capture
{
    public SKImage Image { get; }

    /// <summary>Pixels per point on the display it came from: 1 on a standard display, 1.25
    /// to 2 on a scaled one, and any value between.</summary>
    public double Scale { get; }

    /// <summary>The colour most of the capture's border has. A canvas grown past the capture
    /// is filled with it, so the screenshot looks as if it simply goes on.</summary>
    public SKColor EdgeColor { get; }

    /// <summary>A backdrop gradient's two colours: the capture's commonest colour, then the
    /// commonest one clearly different from it, or a shade of the first when there is none.</summary>
    public SKColor[] GradientColors { get; }

    public Capture(SKImage image, double scale)
    {
        Image = image;
        Scale = scale;
        var colors = new ColorSample(image);
        EdgeColor = colors.Edge;
        GradientColors = colors.Gradient;
    }

    public Size PixelSize => new(Image.Width, Image.Height);

    /// <summary>Whether the capture is mostly dark under <paramref name="rect"/>, in capture pixels.
    /// Past the capture it reads the edge colour the canvas is grown with there.</summary>
    internal bool IsDark(Rect rect)
    {
        var shown = rect.Integral.Intersection(Bounds);
        if (shown.IsNull || shown.Width < 1 || shown.Height < 1) return Palette.Luminance(EdgeColor) < 0.5;
        using var patch = Image.Subset(shown.ToSKRectI());
        return patch is not null && PixelBuffer.From(patch)?.MeanLuminance is { } luminance
            ? luminance < 0.5
            : Palette.Luminance(EdgeColor) < 0.5;
    }
    public Rect Bounds => new(Point.Zero, PixelSize);
    public Size PointSize => new(PixelSize.Width / Scale, PixelSize.Height / Scale);

    /// <summary>The part of a frozen display under <paramref name="rect"/>, given in points
    /// from the display's top left corner. Clipped to the image, and null when nothing of it
    /// is left.</summary>
    public static Capture? Crop(SKImage image, Rect rect, double scale)
    {
        var pixels = new Rect(rect.MinX * scale, rect.MinY * scale, rect.Size.Width * scale, rect.Size.Height * scale)
            .Integral
            .Intersection(new Rect(0, 0, image.Width, image.Height));
        if (pixels.IsNull || pixels.Width < 1 || pixels.Height < 1) return null;
        var cropped = image.Subset(pixels.ToSKRectI());
        return cropped is null ? null : new Capture(cropped, scale);
    }
}

/// <summary>One capture plus everything drawn on it. Plain values, so undo is a stack of these.</summary>
public sealed record Document
{
    private readonly ImmutableArray<Annotation> annotations = [];

    public Capture Capture { get; init; }

    /// <summary>Applied only on export. Null means the whole capture.</summary>
    public Rect? Crop { get; init; }

    /// <summary>Drawn bottom to top, in creation order.</summary>
    public ImmutableArray<Annotation> Annotations
    {
        get => annotations;
        init => annotations = NumberNewShapes(value.IsDefault ? [] : value);
    }

    /// <summary>Null while there is no backdrop.</summary>
    public Backdrop? Backdrop { get; init; }

    /// <summary>The export's size as output pixels per capture pixel, 0.01 to 4. Null follows
    /// the Export setting.</summary>
    public double? Resize { get; init; }

    public const int StepStartMin = 1;
    public const int StepStartMax = 999;

    private readonly int stepStart = StepStartMin;

    /// <summary>Where the steps start counting: 1, or more to carry on from another capture.</summary>
    public int StepStart
    {
        get => stepStart;
        init => stepStart = Math.Clamp(value, StepStartMin, StepStartMax);
    }

    public Document(Capture capture, Rect? crop = null, ImmutableArray<Annotation> annotations = default,
                    Backdrop? backdrop = null, double? resize = null, int stepStart = StepStartMin)
    {
        Capture = capture;
        Crop = crop;
        Annotations = annotations;
        Backdrop = backdrop;
        Resize = resize;
        StepStart = stepStart;
    }

    /// <summary>The same capture, crop, annotations, backdrop and size. The annotation
    /// lists are compared item by item: an immutable array otherwise compares by reference,
    /// and undo would record a step for every unchanged document.</summary>
    public bool Equals(Document? other) =>
        other is not null && ReferenceEquals(Capture, other.Capture) && Crop == other.Crop
        && Annotations.SequenceEqual(other.Annotations) && Backdrop == other.Backdrop && Resize == other.Resize
        && StepStart == other.StepStart;

    public override int GetHashCode() => HashCode.Combine(Capture, Crop, Annotations.Length, Backdrop, Resize, StepStart);

    public double Scale => Capture.Scale;

    /// <summary>What is exported: the crop, or the whole extent.</summary>
    public Rect OutputRect => Crop ?? Extent;

    /// <summary>The output on whole pixels, rounded inwards, which is what an export cuts.</summary>
    public Rect OutputPixelRect
    {
        get
        {
            var rect = OutputRect;
            var left = Math.Ceiling(rect.MinX);
            var top = Math.Ceiling(rect.MinY);
            return new Rect(left, top, Math.Max(1, Math.Floor(rect.MaxX) - left), Math.Max(1, Math.Floor(rect.MaxY) - top));
        }
    }

    /// <summary>The framed output in capture pixels: the output with the backdrop's padding
    /// on every side. Null without a backdrop.</summary>
    public Rect? FramedRect
    {
        get
        {
            if (Backdrop is null) return null;
            var padding = Backdrop.Padding.Points() * Scale;
            return OutputPixelRect.Inset(-padding, -padding);
        }
    }

    /// <summary>Points of margin kept around a shape drawn past the capture's edge.</summary>
    internal const double GrowthMargin = 16;

    /// <summary>The capture, grown to take in every annotation drawn past its edge, with a
    /// margin around it, on whole pixels. Computed rather than stored, so undo and delete
    /// shrink the canvas back as readily as drawing grows it.</summary>
    public Rect Extent
    {
        get
        {
            var margin = GrowthMargin * Scale;
            var bounds = Capture.Bounds;
            // A hidden shape is left out of the output, so it grows nothing.
            return Annotations.Where(a => !a.IsHidden).Aggregate(bounds, (extent, annotation) =>
            {
                return bounds.Contains(annotation.GrowthBounds(Scale))
                    ? extent
                    : extent.Union(annotation.Bounds(Scale).Inset(-margin, -margin));
            }).Integral;
        }
    }

    /// <summary>The crop, taking in every shape that changed since <paramref name="before"/> and now
    /// reaches past it, with the margin the canvas gives a shape drawn past the capture's edge, and
    /// keeping <paramref name="ratio"/> when there is one. Measured from the crop
    /// <paramref name="before"/> had, so a shape dragged out and back leaves it as it was. A shape
    /// wholly outside is somewhere the crop leaves out, as one cropped away is, and blur, pixelate and
    /// erase hide what is under them rather than add anything: neither moves it.</summary>
    public Document CropFollowingShapes(Document before, double? ratio)
    {
        if (before.Crop is not { } crop) return this;
        var earlier = before.Annotations.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        var changed = Annotations.Where(a => !earlier.TryGetValue(a.Id, out var was) || was != a).ToList();
        if (changed.Count == 0) return this;
        var margin = GrowthMargin * Scale;
        var grown = crop;
        foreach (var annotation in changed.Where(a => !a.IsHidden && !a.Tool.HidesCapture()))
        {
            var reach = annotation.GrowthBounds(Scale);
            if (!reach.Intersects(crop) || crop.Contains(reach)) continue;
            grown = grown.Union(annotation.Bounds(Scale).Inset(-margin, -margin));
        }
        if (grown == crop) return this with { Crop = crop };
        // Grown to the ratio past the canvas's edge, it moves back in rather than lose the ratio.
        if (ratio is { } value) grown = grown.Expanded(value).ShiftedInto(Extent);
        return this with { Crop = grown.Intersection(Extent).WholePixels };
    }

    public Annotation? Annotation(Guid id) => Annotations.FirstOrDefault(a => a.Id == id);

    /// <summary>Not stored: the start plus the number of shown steps of its kind before this one,
    /// so deleting or hiding a step renumbers every step after it. Numbered and lettered steps
    /// count apart. Null for a hidden step, which shows no number.</summary>
    public int? StepNumber(Guid id)
    {
        if (Annotation(id) is not { Kind: AnnotationKind.Step, IsHidden: false } step) return null;
        var number = StepStart - 1;
        foreach (var annotation in Annotations)
        {
            if (annotation.Kind is not AnnotationKind.Step || annotation.IsHidden || annotation.Style.Letters != step.Style.Letters)
                continue;
            number++;
            if (annotation.Id == id) return number;
        }
        return null;
    }

    /// <summary>What a step shows: its number, or for a lettered step the letters at that place.</summary>
    public string? StepLabel(Guid id) =>
        StepNumber(id) is not { } number ? null
        : Annotation(id)!.Style.Letters ? Letters(number)
        : number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>1 is A, 26 is Z and 27 is AA, the way spreadsheet columns count.</summary>
    internal static string Letters(int number)
    {
        var label = "";
        for (var rest = number; rest > 0; rest = (rest - 1) / 26) label = (char)('A' + (rest - 1) % 26) + label;
        return label;
    }

    /// <summary>What the layers panel calls a shape: its tool and its count among that tool's
    /// shapes, its text's first line, a step's number, a measurement's length as its tag gives it.</summary>
    public string LayerName(Guid id)
    {
        if (Annotation(id) is not { } annotation) return "";
        if (KindName(annotation) is { } kind)
        {
            // Counted among the shapes of its kind in the order they were drawn, so no two rows
            // share a name and reordering renames nothing.
            var same = Annotations.Select((a, index) => (a, index)).Where(p => KindName(p.a) == kind)
                .OrderBy(p => p.a.Serial).ThenBy(p => p.index).ToList();
            return $"{kind} {same.FindIndex(p => p.a.Id == id) + 1}";
        }
        return annotation.Kind switch
        {
            AnnotationKind.Text(_, var text) => FirstLine(text) is { Length: > 0 } line ? line : "Text",
            AnnotationKind.Step => StepLabel(id) is { } label ? $"Step {label}" : "Step",
            AnnotationKind.Measure(var from, var to) => "Measure " + MeasureReading.Label(from.Distance(to), Scale),
            _ => "",
        };
    }

    /// <summary>The name a shape shares with every other of its kind, before its number; null for
    /// text, steps and measurements, which are named by what they say.</summary>
    private static string? KindName(Annotation annotation) => annotation.Kind switch
    {
        AnnotationKind.Text or AnnotationKind.Step or AnnotationKind.Measure => null,
        // Their tool titles carry a warning that has no place in a list of names.
        AnnotationKind.Blur => "Blur",
        AnnotationKind.Pixelate => "Pixelate",
        AnnotationKind.Erase => "Erase",
        _ => annotation.Tool.Title(),
    };

    /// <summary>Each shape without a number takes the next one, in list order: a new shape, a
    /// duplicate, or every shape of a file from before numbers, counted bottom up.</summary>
    private static ImmutableArray<Annotation> NumberNewShapes(ImmutableArray<Annotation> list)
    {
        if (!list.Any(a => a.Serial == 0)) return list;
        var next = list.Max(a => a.Serial) + 1;
        var numbered = list.ToBuilder();
        for (var i = 0; i < numbered.Count; i++)
            if (numbered[i].Serial == 0) numbered[i] = numbered[i] with { Serial = next++ };
        return numbered.ToImmutable();
    }

    /// <summary>The first line with anything on it, trimmed, at most 40 characters as a person
    /// counts them, so an emoji is never cut in half. As the Mac names text.</summary>
    private static string FirstLine(string text)
    {
        var line = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        var info = new System.Globalization.StringInfo(line);
        return info.LengthInTextElements > 40 ? info.SubstringByTextElements(0, 40) : line;
    }

    /// <summary>The topmost shown annotation under <paramref name="point"/>. A locked one
    /// counts: it can be selected.</summary>
    public Guid? Topmost(Point point) =>
        Annotations.LastOrDefault(a => !a.IsHidden && a.Contains(point, Scale))?.Id;

    /// <summary>What a click with a drawing tool picks up instead of drawing: the topmost
    /// annotation it lands on, or whose hover border it lands on. Landing on means inside for
    /// whatever changes its middle, a blur, an erase, text, an image, a filled box, and on the
    /// stroke for lines and outlines, whose empty middle is still drawn in. A spotlight's clear
    /// middle is where arrows and boxes go, so only its border counts. Locked and hidden
    /// shapes are never picked up: the click draws over them.</summary>
    public Guid? PickUp(Point point, double reach)
    {
        var border = BorderHit(point, reach, pickable: true);
        return Annotations.LastOrDefault(a => !a.IsLocked && !a.IsHidden
            && (a.Id == border || (a.Kind is not AnnotationKind.Spotlight && a.Contains(point, Scale))))?.Id;
    }

    /// <summary>The topmost shown annotation whose hover border runs under
    /// <paramref name="point"/>, leaving out locked ones when <paramref name="pickable"/>. The
    /// border is drawn half a reach outside the annotation's bounds, and anything within a
    /// reach of it counts, so the border can be clicked as readily as it is seen.</summary>
    public Guid? BorderHit(Point point, double reach, bool pickable = false)
    {
        if (reach <= 0) return null;
        return Annotations.LastOrDefault(a =>
        {
            if (a.IsHidden || (pickable && a.IsLocked)) return false;
            var border = a.Bounds(Scale).Inset(-reach / 2, -reach / 2);
            var inner = border.Inset(reach, reach);
            var inside = !inner.IsNull && inner.Width > 0 && inner.Height > 0 && inner.Contains(point);
            return border.Inset(-reach, -reach).Contains(point) && !inside;
        })?.Id;
    }

    public Document Replacing(Annotation annotation)
    {
        var index = Annotations.FindIndex(a => a.Id == annotation.Id);
        return index < 0 ? this : this with { Annotations = Annotations.SetItem(index, annotation) };
    }

    public Document Removing(Guid id) => this with { Annotations = Annotations.RemoveAll(a => a.Id == id) };
}

internal static class ImmutableArrayExtensions
{
    public static int FindIndex<T>(this ImmutableArray<T> array, Func<T, bool> match)
    {
        for (var index = 0; index < array.Length; index++)
            if (match(array[index])) return index;
        return -1;
    }
}
