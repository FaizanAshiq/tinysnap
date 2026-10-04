using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>What <c>edits.json</c> holds: everything about a document except its capture
/// pixels, which the library keeps beside it as <c>original.png</c>.</summary>
public sealed record ArchivedEdits(DateTimeOffset Captured, double Scale, Rect? Crop, ImmutableArray<Annotation> Annotations,
                                   Backdrop? Backdrop, double? Resize, int StepStart = Document.StepStartMin);

/// <summary>An <c>edits.json</c> that cannot be rebuilt exactly.</summary>
public sealed class ArchiveException(string reason) : Exception(reason);

/// <summary>A document to and from <c>edits.json</c>, plus one PNG per pasted image.
/// Coordinates stay capture pixels, y growing downward, as everywhere in Core.</summary>
public static class DocumentArchive
{
    public const int Version = 1;

    /// <summary>ISO 8601 with a zone, and no fractions, which is all the Mac writes or reads.</summary>
    private static readonly string[] DateFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:sszzz"];

    /// <summary>The JSON, and the pasted images it names, keyed by file name.</summary>
    public static (byte[] Json, IReadOnlyDictionary<string, SKImage> Images) Encode(Document document, DateTimeOffset captured)
    {
        var images = new Dictionary<string, SKImage>();
        var items = new JsonArray();
        foreach (var annotation in document.Annotations)
        {
            var item = new JsonObject { ["id"] = Json.Uuid(annotation.Id), ["style"] = annotation.Style.ToJson() };
            // Only a tag slid off its middle is written, so everything else reads as before.
            if (annotation.LabelAt != 0.5) item["labelAt"] = annotation.LabelAt;
            // Written only when on, so an older Tinysnap reads the file as before.
            if (annotation.IsLocked) item["locked"] = true;
            if (annotation.IsHidden) item["hidden"] = true;
            item["serial"] = annotation.Serial;
            void Ends(string kind, Point from, Point to)
            {
                item["kind"] = kind;
                item["from"] = Pair(from);
                item["to"] = Pair(to);
            }
            void Boxed(string kind, Rect rect)
            {
                item["kind"] = kind;
                item["rect"] = Box(rect);
            }
            switch (annotation.Kind)
            {
                case AnnotationKind.Arrow(var from, var to): Ends("arrow", from, to); break;
                case AnnotationKind.Line(var from, var to): Ends("line", from, to); break;
                case AnnotationKind.Measure(var from, var to): Ends("measure", from, to); break;
                case AnnotationKind.Highlighter(var from, var to): Ends("highlighter", from, to); break;
                case AnnotationKind.Rectangle(var rect): Boxed("rectangle", rect); break;
                case AnnotationKind.Oval(var rect): Boxed("oval", rect); break;
                case AnnotationKind.Spotlight(var rect): Boxed("spotlight", rect); break;
                case AnnotationKind.Blur(var rect): Boxed("blur", rect); break;
                case AnnotationKind.Pixelate(var rect): Boxed("pixelate", rect); break;
                case AnnotationKind.Erase(var rect): Boxed("erase", rect); break;
                case AnnotationKind.Text(var origin, var text):
                    item["kind"] = "text";
                    item["origin"] = Pair(origin);
                    item["string"] = text;
                    break;
                case AnnotationKind.Freehand(var points):
                    item["kind"] = "freehand";
                    item["points"] = new JsonArray([.. points.Select(p => (JsonNode)Pair(p))]);
                    break;
                case AnnotationKind.HighlighterPath(var points):
                    // The ends as well, so an older Tinysnap draws it as a straight highlight.
                    Ends("highlighter", points.IsEmpty ? Point.Zero : points[0], points.IsEmpty ? Point.Zero : points[^1]);
                    item["points"] = new JsonArray([.. points.Select(p => (JsonNode)Pair(p))]);
                    break;
                case AnnotationKind.Step(var center):
                    item["kind"] = "step";
                    item["center"] = Pair(center);
                    break;
                case AnnotationKind.Magnifier(var center, var radius, var zoom):
                    item["kind"] = "magnifier";
                    item["center"] = Pair(center);
                    item["radius"] = radius;
                    item["zoom"] = zoom;
                    break;
                case AnnotationKind.Image(var rect, var pasted):
                    var name = $"pasted-{Json.Uuid(annotation.Id)}.png";
                    images[name] = pasted.Image;
                    Boxed("image", rect);
                    item["file"] = name;
                    break;
            }
            items.Add((JsonNode)item);
        }

        var root = new JsonObject
        {
            ["version"] = Version,
            ["captured"] = captured.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["scale"] = document.Scale,
            ["annotations"] = items,
        };
        // Absent optionals are left out, never written as null, as the Mac's encoder does.
        if (document.Crop is { } crop) root["crop"] = Box(crop);
        if (document.Resize is { } resize) root["resize"] = resize;
        // Written only when it is not 1, so an older Tinysnap reads the file as before.
        if (document.StepStart != Document.StepStartMin) root["stepStart"] = document.StepStart;
        if (document.Backdrop is { } backdrop)
        {
            var stored = backdrop.ToJson();
            if (backdrop is { Fill: BackdropFill.Wallpaper, Wallpaper: { } wallpaper })
            {
                // Named for the wallpaper, whose pixels never change, so it is written once.
                var name = $"backdrop-{Json.Uuid(wallpaper.Id)}.png";
                images[name] = wallpaper.Image.Image;
                stored["file"] = name;
            }
            root["backdrop"] = stored;
        }
        return (Json.WriteBytes(root), images);
    }

    /// <summary>Throws for anything it cannot rebuild exactly, so the library can fall back to
    /// the flat image rather than open a document with pieces missing. Strict for everything
    /// the document needs, lenient for the backdrop and the size: one that cannot be read is
    /// left off, and the rest of the entry opens as it was.</summary>
    public static ArchivedEdits Decode(byte[] json, Func<string, SKImage?> image)
    {
        if (Json.Parse(json) is not JsonObject root) throw new ArchiveException("not a JSON object");
        var version = Json.Integer(root, "version") ?? throw Missing("version");
        if (version != Version) throw new ArchiveException($"unsupported version {version}");
        var captured = Json.String(root, "captured") is { } text
            && DateTimeOffset.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                                            out var date)
            ? date
            : throw Missing("captured");
        var scale = Json.Number(root, "scale") ?? throw Missing("scale");
        // Pixels per point: 1 to 3 on real displays. Anything outside a generous range gives
        // the capture no size or an absurd one, which the editor cannot lay out.
        if (!double.IsFinite(scale) || scale < 1 || scale > 8) throw new ArchiveException($"bad scale {scale}");
        var crop = OptionalBox(root, "crop");
        var list = Json.Array(root, "annotations") ?? throw Missing("annotations");
        var annotations = list.Select(node => Item(node, image)).ToImmutableArray();
        // A size past the limits is as unreadable as a word.
        var resize = Json.Number(root, "resize") is { } r && r >= DocumentSizing.ResizeMin && r <= DocumentSizing.ResizeMax
            ? r
            : (double?)null;
        var stepStart = Json.Integer(root, "stepStart") is { } s && s >= Document.StepStartMin && s <= Document.StepStartMax
            ? (int)s
            : Document.StepStartMin;
        return new ArchivedEdits(captured, scale, crop, annotations, StoredBackdrop(root, image), resize, stepStart);
    }

    /// <summary>A wallpaper whose file is gone turns the fill into the gradient, rather than
    /// losing the entry over one picture.</summary>
    private static Backdrop? StoredBackdrop(JsonObject root, Func<string, SKImage?> image)
    {
        if (Json.Object(root, "backdrop") is not { } stored) return null;
        var backdrop = Backdrop.FromJson(stored);
        if (backdrop.Fill != BackdropFill.Wallpaper) return backdrop;
        if (Json.String(stored, "file") is not { } name || image(name) is not { } pixels)
            return backdrop with { Fill = BackdropFill.Gradient };
        var idText = name.StartsWith("backdrop-", StringComparison.Ordinal) && name.EndsWith(".png", StringComparison.Ordinal)
            ? name["backdrop-".Length..^".png".Length]
            : "";
        var id = Guid.TryParseExact(idText, "D", out var parsed) ? parsed : Guid.NewGuid();
        return backdrop with { Wallpaper = new BackdropWallpaper(id, new PastedImage(pixels)) };
    }

    /// <summary>One annotation. Every field is read first and one of the wrong type throws,
    /// used by its kind or not, as the Mac's decoder does.</summary>
    private static Annotation Item(JsonNode? node, Func<string, SKImage?> image)
    {
        if (node is not JsonObject item) throw new ArchiveException("an annotation is not an object");
        var id = Json.String(item, "id") is { } idText && Guid.TryParseExact(idText, "D", out var parsed)
            ? parsed
            : throw Missing("id");
        var kindName = Json.String(item, "kind") ?? throw Missing("kind");
        var style = Json.Object(item, "style") ?? throw Missing("style");
        var from = OptionalNumbers(item, "from");
        var to = OptionalNumbers(item, "to");
        var origin = OptionalNumbers(item, "origin");
        var center = OptionalNumbers(item, "center");
        var rect = OptionalBox(item, "rect");
        var text = OptionalString(item, "string");
        var points = OptionalPoints(item, "points");
        var radius = OptionalNumber(item, "radius");
        var zoom = OptionalNumber(item, "zoom");
        var file = OptionalString(item, "file");
        var labelAt = OptionalNumber(item, "labelAt") is { } at && at is >= 0 and <= 1 ? at : 0.5;

        static Point Pt(double[]? pair, string name) => pair is { Length: 2 } ? new Point(pair[0], pair[1]) : throw Missing(name);
        Rect Box() => rect ?? throw Missing("rect");

        AnnotationKind kind = kindName switch
        {
            "arrow" => new AnnotationKind.Arrow(Pt(from, "from"), Pt(to, "to")),
            "line" => new AnnotationKind.Line(Pt(from, "from"), Pt(to, "to")),
            "measure" => new AnnotationKind.Measure(Pt(from, "from"), Pt(to, "to")),
            "highlighter" when points is { Length: >= 2 } =>
                new AnnotationKind.HighlighterPath([.. points.Select(p => Pt(p, "points"))]),
            "highlighter" => new AnnotationKind.Highlighter(Pt(from, "from"), Pt(to, "to")),
            "rectangle" => new AnnotationKind.Rectangle(Box()),
            "oval" => new AnnotationKind.Oval(Box()),
            "spotlight" => new AnnotationKind.Spotlight(Box()),
            "blur" => new AnnotationKind.Blur(Box()),
            "pixelate" => new AnnotationKind.Pixelate(Box()),
            "erase" => new AnnotationKind.Erase(Box()),
            "text" => new AnnotationKind.Text(Pt(origin, "origin"), text ?? throw Missing("string")),
            "freehand" => new AnnotationKind.Freehand([.. (points ?? throw Missing("points")).Select(p => Pt(p, "points"))]),
            "step" => new AnnotationKind.Step(Pt(center, "center")),
            "magnifier" => new AnnotationKind.Magnifier(Pt(center, "center"), radius ?? throw Missing("radius"),
                                                        zoom ?? throw Missing("zoom")),
            "image" => PastedKind(file ?? throw Missing("file"), image, Box),
            _ => throw new ArchiveException($"unknown kind {kindName}"),
        };
        return new Annotation(id, kind, Style.FromJson(style), labelAt)
        {
            IsLocked = Json.Bool(item, "locked") ?? false,
            IsHidden = Json.Bool(item, "hidden") ?? false,
            // The draw order that numbers a layer name; files from before have none.
            Serial = Math.Max(0, Json.Integer(item, "serial") ?? 0),
        };
    }

    private static AnnotationKind PastedKind(string name, Func<string, SKImage?> image, Func<Rect> rect)
    {
        var pixels = image(name) ?? throw new ArchiveException($"missing image {name}");
        return new AnnotationKind.Image(rect(), new PastedImage(pixels));
    }

    private static ArchiveException Missing(string field) => new($"missing or unreadable {field}");

    // Optional fields: absent or null reads as absent, as decodeIfPresent does, and anything
    // else of the wrong type throws.

    private static bool Present(JsonObject o, string key) => o[key] is not null;

    private static double? OptionalNumber(JsonObject o, string key) =>
        !Present(o, key) ? null : Json.Number(o, key) ?? throw Missing(key);

    private static string? OptionalString(JsonObject o, string key) =>
        !Present(o, key) ? null : Json.String(o, key) ?? throw Missing(key);

    private static double[]? OptionalNumbers(JsonObject o, string key) =>
        !Present(o, key) ? null : Numbers(o[key]) ?? throw Missing(key);

    private static double[][]? OptionalPoints(JsonObject o, string key)
    {
        if (!Present(o, key)) return null;
        if (o[key] is not JsonArray list) throw Missing(key);
        return [.. list.Select(node => Numbers(node) ?? throw Missing(key))];
    }

    private static double[]? Numbers(JsonNode? node)
    {
        if (node is not JsonArray list) return null;
        var numbers = list.Select(Json.Number).ToArray();
        return numbers.All(n => n is not null) ? [.. numbers.Select(n => n!.Value)] : null;
    }

    /// <summary>A rectangle as <c>{"x", "y", "width", "height"}</c>, which reads better by hand
    /// than nested arrays.</summary>
    private static Rect? OptionalBox(JsonObject o, string key)
    {
        if (!Present(o, key)) return null;
        return o[key] is JsonObject box && Json.Number(box, "x") is { } x && Json.Number(box, "y") is { } y
            && Json.Number(box, "width") is { } width && Json.Number(box, "height") is { } height
            ? new Rect(x, y, width, height)
            : throw Missing(key);
    }

    private static JsonObject Box(Rect rect) => new()
    {
        ["x"] = rect.MinX,
        ["y"] = rect.MinY,
        ["width"] = rect.Size.Width,
        ["height"] = rect.Size.Height,
    };

    private static JsonArray Pair(Point point) => new(JsonValue.Create(point.X), JsonValue.Create(point.Y));
}
