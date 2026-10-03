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
        init => annotations = value.IsDefault ? [] : value;
    }

    /// <summary>Null while there is no backdrop.</summary>
    public Backdrop? Backdrop { get; init; }

    /// <summary>The export's size as output pixels per capture pixel, 0.01 to 4. Null follows
    /// the Export setting.</summary>
    public double? Resize { get; init; }

    public Document(Capture capture, Rect? crop = null, ImmutableArray<Annotation> annotations = default,
                    Backdrop? backdrop = null, double? resize = null)
    {
        Capture = capture;
        Crop = crop;
        Annotations = annotations;
        Backdrop = backdrop;
        Resize = resize;
    }

    /// <summary>The same capture, crop, annotations, backdrop and size. The annotation
    /// lists are compared item by item: an immutable array otherwise compares by reference,
    /// and undo would record a step for every unchanged document.</summary>
    public bool Equals(Document? other) =>
        other is not null && ReferenceEquals(Capture, other.Capture) && Crop == other.Crop
        && Annotations.SequenceEqual(other.Annotations) && Backdrop == other.Backdrop && Resize == other.Resize;

    public override int GetHashCode() => HashCode.Combine(Capture, Crop, Annotations.Length, Backdrop, Resize);

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
                var box = annotation.Bounds(Scale);
                return bounds.Contains(box) ? extent : extent.Union(box.Inset(-margin, -margin));
            }).Integral;
        }
    }

    public Annotation? Annotation(Guid id) => Annotations.FirstOrDefault(a => a.Id == id);

    /// <summary>Not stored: 1 plus the number of shown steps before this one, so deleting or
    /// hiding a step renumbers every step after it. Null for a hidden step, which shows no
    /// number.</summary>
    public int? StepNumber(Guid id)
    {
        var number = 0;
        foreach (var annotation in Annotations)
        {
            if (annotation.Kind is not AnnotationKind.Step || annotation.IsHidden) continue;
            number++;
            if (annotation.Id == id) return number;
        }
        return null;
    }

    /// <summary>What the layers panel calls a shape: its tool, its text's first line, a step's
    /// number, a measurement's length as its tag gives it.</summary>
    public string LayerName(Guid id)
    {
        if (Annotation(id) is not { } annotation) return "";
        return annotation.Kind switch
        {
            AnnotationKind.Text(_, var text) => FirstLine(text) is { Length: > 0 } line ? line : "Text",
            AnnotationKind.Step => StepNumber(id) is { } number ? $"Step {number}" : "Step",
            AnnotationKind.Measure(var from, var to) => "Measure " + MeasureReading.Label(from.Distance(to), Scale),
            // Their tool titles carry a warning that has no place in a list of names.
            AnnotationKind.Blur => "Blur",
            AnnotationKind.Pixelate => "Pixelate",
            AnnotationKind.Erase => "Erase",
            _ => annotation.Tool.Title(),
        };
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
