using System.Text;
using System.Text.Json.Nodes;

namespace Tinysnap.Core.Tests;

public class DocumentArchiveTests
{
    private static readonly DateTimeOffset Captured = DateTimeOffset.FromUnixTimeSeconds(1_790_300_530);

    private const string Valid = """{"version": 1, "captured": "2026-09-25T07:42:10Z", "scale": 2, "annotations": []}""";

    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    private static JsonObject Root(byte[] json) => Json.Parse(json)!.AsObject();

    private static ArchivedEdits Decode(string json) => DocumentArchive.Decode(Encoding.UTF8.GetBytes(json), _ => null);

    private static Backdrop WallpaperBackdrop() =>
        new(BackdropFill.Wallpaper, "#007AFF", BackdropPadding.Large, CornerSize.Square, BackdropShadow.Strong,
            new BackdropWallpaper(Guid.NewGuid(), new PastedImage(Fixture.CaptureImage(8, 4, Fixture.Green))));

    [Fact]
    public void AMeasurementComesBack()
    {
        var measure = Fixture.Annotation(new AnnotationKind.Measure(new Point(288, 200), new Point(320, 200)));
        var document = new Document(Fixture.Capture(400, 300, 2), annotations: [measure]);
        var (json, images) = DocumentArchive.Encode(document, Captured);
        Assert.Empty(images);
        Assert.Equal("measure", Json.String(Root(json)["annotations"]![0]!.AsObject(), "kind"));
        var edits = DocumentArchive.Decode(json, _ => null);
        Assert.Equal(new[] { measure }, edits.Annotations);
    }

    [Fact]
    public void LockedAndHiddenComeBackAndAreWrittenOnlyWhenOn()
    {
        var locked = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(10, 10, 40, 30))) with { IsLocked = true };
        var hidden = Fixture.Annotation(new AnnotationKind.Oval(new Rect(60, 10, 40, 30))) with { IsHidden = true };
        var plain = Fixture.Annotation(new AnnotationKind.Line(new Point(5, 5), new Point(90, 90)));
        var document = new Document(Fixture.Capture(200, 100), annotations: [locked, hidden, plain]);
        var (json, _) = DocumentArchive.Encode(document, Captured);
        var items = Root(json)["annotations"]!.AsArray().Select(item => item!.AsObject()).ToList();
        Assert.Equal([true, false, false], items.Select(item => item.ContainsKey("locked")));
        Assert.Equal([false, true, false], items.Select(item => item.ContainsKey("hidden")));
        Assert.Equal(new[] { locked, hidden, plain }, DocumentArchive.Decode(json, _ => null).Annotations);
    }

    [Fact]
    public void AFileFromBeforeLayersOpensUnlockedAndShown()
    {
        var annotation = Decode("""{"version":1,"captured":"2026-10-02T16:34:05Z","scale":2,"annotations":[{"id":"F86ED62D-E65F-492A-9835-C9A1EF940F71","kind":"pixelate","rect":{"x":34,"y":240,"width":300,"height":200},"style":{"colorHex":"#FF3B30","size":"large"}}]}""")
            .Annotations.Single();
        Assert.False(annotation.IsLocked || annotation.IsHidden);
    }

    [Fact]
    public void AMeasurementsMovedTagComesBack()
    {
        var moved = Annotation.New(new AnnotationKind.Measure(new Point(150, 20), new Point(150, 180)), Fixture.Red, 0.3);
        var centred = Fixture.Annotation(new AnnotationKind.Measure(new Point(50, 100), new Point(250, 100)));
        var document = new Document(Fixture.Capture(300, 200), annotations: [moved, centred]);
        var (json, _) = DocumentArchive.Encode(document, Captured);
        // Only a tag off its middle is written, so older files and centred tags read the same.
        Assert.Equal(2, Encoding.UTF8.GetString(json).Split("labelAt").Length);
        var edits = DocumentArchive.Decode(json, _ => null);
        Assert.Equal(new[] { 0.3, 0.5 }, edits.Annotations.Select(a => a.LabelAt));
    }

    [Fact]
    public void ABackdropAndItsWallpaperComeBack()
    {
        var backdrop = WallpaperBackdrop();
        var document = new Document(Fixture.Capture(40, 30), backdrop: backdrop);
        var (json, images) = DocumentArchive.Encode(document, Captured);
        var name = $"backdrop-{Upper(backdrop.Wallpaper!.Id)}.png";
        Assert.Equal(new[] { name }, images.Keys);

        var edits = DocumentArchive.Decode(json, file => images.GetValueOrDefault(file));
        Assert.Equal(backdrop, edits.Backdrop);
        Assert.Equal(8, edits.Backdrop?.Wallpaper?.Image.Image.Width);
    }

    [Fact]
    public void AMissingWallpaperFileTurnsTheFillIntoAGradient()
    {
        var document = new Document(Fixture.Capture(40, 30), backdrop: WallpaperBackdrop());
        var (json, _) = DocumentArchive.Encode(document, Captured);
        var edits = DocumentArchive.Decode(json, _ => null);
        Assert.Equal(BackdropFill.Gradient, edits.Backdrop?.Fill);
        Assert.Null(edits.Backdrop?.Wallpaper);
        Assert.Equal(BackdropPadding.Large, edits.Backdrop?.Padding);
    }

    [Fact]
    public void ASizeComesBackAndAFileWithoutOneFollowsTheSetting()
    {
        var document = new Document(Fixture.Capture(40, 30, 2), resize: 0.5);
        var (json, _) = DocumentArchive.Encode(document, Captured);
        Assert.Equal(0.5, Json.Number(Root(json), "resize"));
        Assert.Equal(0.5, DocumentArchive.Decode(json, _ => null).Resize);

        var (plain, _) = DocumentArchive.Encode(new Document(Fixture.Capture(40, 30)), Captured);
        Assert.False(Root(plain).ContainsKey("resize"));
        Assert.Null(DocumentArchive.Decode(plain, _ => null).Resize);
    }

    [Fact]
    public void AnUnreadableSizeIsLeftOffAndTheRestIntact()
    {
        foreach (var bad in new[] { "\"big\"", "9", "0", "-1" })
        {
            var edits = Decode(Valid[..^1] + ", \"resize\": " + bad + "}");
            Assert.Null(edits.Resize);
            Assert.Equal(2, edits.Scale);
        }
    }

    [Fact]
    public void AnUnreadableBackdropLeavesItOffAndTheRestIntact()
    {
        var edits = Decode(Valid[..^1] + ", \"backdrop\": 7}");
        Assert.Null(edits.Backdrop);
        Assert.Equal(2, edits.Scale);
    }

    private static Annotation[] EveryKind(PastedImage pasted)
    {
        var blue = new Style("#007AFF", StyleSize.Large, filled: true, corners: CornerSize.Full);
        return
        [
            Fixture.Annotation(new AnnotationKind.Arrow(new Point(10, 20), new Point(110.5, 80))),
            Fixture.Annotation(new AnnotationKind.Line(new Point(5, 5), new Point(50, 5))),
            Annotation.New(new AnnotationKind.Rectangle(new Rect(20, 30, 100, 60)), blue),
            Fixture.Annotation(new AnnotationKind.Oval(new Rect(40, 40, 30, 20))),
            Fixture.Annotation(new AnnotationKind.Text(new Point(12, 14), "Two\nlines")),
            Fixture.Annotation(new AnnotationKind.Highlighter(new Point(0, 90), new Point(90, 90))),
            Fixture.Annotation(new AnnotationKind.Freehand([new Point(1, 2), new Point(3, 4), new Point(5, 7)])),
            Fixture.Annotation(new AnnotationKind.Step(new Point(70, 70))),
            Fixture.Annotation(new AnnotationKind.Spotlight(new Rect(10, 10, 40, 40))),
            Fixture.Annotation(new AnnotationKind.Magnifier(new Point(150, 100), 30, 2.5)),
            Fixture.Annotation(new AnnotationKind.Image(new Rect(100, 10, 40, 30), pasted)),
            Fixture.Annotation(new AnnotationKind.Blur(new Rect(0, 0, 20, 20))),
            Fixture.Annotation(new AnnotationKind.Pixelate(new Rect(20, 0, 20, 20))),
            Fixture.Annotation(new AnnotationKind.Erase(new Rect(40, 0, 20, 20))),
        ];
    }

    [Fact]
    public void EveryKindStyleCropAndPastedImageComeBack()
    {
        var pasted = new PastedImage(Fixture.CaptureImage(8, 6, Fixture.Blue));
        var document = new Document(Fixture.Capture(200, 120, 2), new Rect(4, 6, 150, 100), [.. EveryKind(pasted)]);

        var (json, images) = DocumentArchive.Encode(document, Captured);
        var edits = DocumentArchive.Decode(json, file => images.GetValueOrDefault(file));

        Assert.Equal(Captured, edits.Captured);
        Assert.Equal(2, edits.Scale);
        Assert.Equal(document.Crop, edits.Crop);
        // A pasted image comes back as new pixels, so it is swapped for the original to
        // compare everything else exactly.
        var restored = edits.Annotations.Select(annotation =>
        {
            if (annotation.Kind is not AnnotationKind.Image(var rect, var image)) return annotation;
            Assert.True(image.Image.Width == 8 && image.Image.Height == 6);
            return annotation with { Kind = new AnnotationKind.Image(rect, pasted) };
        });
        Assert.Equal(document.Annotations, restored);
    }

    [Fact]
    public void EachPastedImageIsNamedForItsAnnotation()
    {
        var pasted = new PastedImage(Fixture.CaptureImage(4, 4));
        var annotation = Fixture.Annotation(new AnnotationKind.Image(new Rect(0, 0, 4, 4), pasted));
        var document = new Document(Fixture.Capture(20, 20), annotations: [annotation]);
        var (_, images) = DocumentArchive.Encode(document, Captured);
        Assert.Equal(new[] { $"pasted-{Upper(annotation.Id)}.png" }, images.Keys);
    }

    [Fact]
    public void TheExampleInTheSpecDecodes()
    {
        var json = """
        {
          "version": 1,
          "captured": "2026-09-25T07:42:10+05:00",
          "scale": 2,
          "crop": { "x": 40, "y": 20, "width": 1100, "height": 740 },
          "annotations": [
            {
              "id": "6F1C2E7A-0D3B-4C55-9E0B-6A1D5B2F9C10",
              "kind": "arrow",
              "from": [120, 610],
              "to": [470, 330],
              "style": { "colorHex": "#FF3B30", "size": "medium", "filled": false, "corners": "medium" }
            },
            {
              "id": "0B7E54C2-91A4-4F0E-8C3D-2E6F1A9B7D44",
              "kind": "image",
              "rect": { "x": 700, "y": 90, "width": 320, "height": 200 },
              "file": "pasted-0B7E54C2-91A4-4F0E-8C3D-2E6F1A9B7D44.png",
              "style": { "colorHex": "#FF3B30", "size": "medium", "filled": false, "corners": "square" }
            }
          ]
        }
        """;
        var asked = new List<string>();
        var edits = DocumentArchive.Decode(Encoding.UTF8.GetBytes(json), name =>
        {
            asked.Add(name);
            return Fixture.CaptureImage(2, 2);
        });
        Assert.Equal(2, edits.Scale);
        Assert.Equal(new Rect(40, 20, 1100, 740), edits.Crop);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_790_304_130), edits.Captured);
        Assert.Equal(new AnnotationKind.Arrow(new Point(120, 610), new Point(470, 330)), edits.Annotations[0].Kind);
        Assert.Equal(CornerSize.Square, edits.Annotations[^1].Style.Corners);
        Assert.Equal(new[] { "pasted-0B7E54C2-91A4-4F0E-8C3D-2E6F1A9B7D44.png" }, asked);
    }

    [Fact]
    public void AnythingItCanNotRebuildExactlyThrows()
    {
        Decode(Valid);

        var otherVersion = Valid.Replace("\"version\": 1", "\"version\": 2");
        var unknownKind = Valid.Replace("[]", """[{"id": "6F1C2E7A-0D3B-4C55-9E0B-6A1D5B2F9C10", "kind": "sticker", "style": {}}]""");
        var missingField = Valid.Replace("[]", """[{"id": "6F1C2E7A-0D3B-4C55-9E0B-6A1D5B2F9C10", "kind": "arrow", "from": [1, 2], "style": {}}]""");
        var missingImage = Valid.Replace("[]", """[{"id": "6F1C2E7A-0D3B-4C55-9E0B-6A1D5B2F9C10", "kind": "image", "rect": {"x": 0, "y": 0, "width": 4, "height": 4}, "file": "pasted-gone.png", "style": {}}]""");
        // A scale that would give the capture no size, or an absurd one, crashed the editor.
        var badScales = new[] { "0", "-2", "1e300" }.Select(s => Valid.Replace("\"scale\": 2", $"\"scale\": {s}"));
        foreach (var broken in new[] { otherVersion, unknownKind, missingField, missingImage, "{", "" }.Concat(badScales))
            Assert.ThrowsAny<Exception>(() => Decode(broken));
    }

    [Fact]
    public void ANumberPastWhatADoubleHoldsIsUnreadable()
    {
        // .NET reads 1e400 as infinity and took it as a coordinate. The Mac refuses the file,
        // and the library then opens the flat image.
        var huge = Valid.Replace("[]", """[{"id": "6F1C2E7A-0D3B-4C55-9E0B-6A1D5B2F9C10", "kind": "arrow", "from": [1e400, 2], "to": [3, 4], "style": {}}]""");
        Assert.Throws<ArchiveException>(() => Decode(huge));
    }

    [Fact]
    public void ReadsTheMacsSpacingOffsetsUuidCaseAndUnknownKeys()
    {
        var json = """
        {
          "annotations" : [
            { "id" : "6f1c2e7a-0d3b-4c55-9e0b-6a1d5b2f9c10", "kind" : "line", "from" : [ 1, 2 ], "to" : [ 3.5, 4 ],
              "style" : { "colorHex" : "#FF3B30" }, "addedLater" : true }
          ],
          "captured" : "2026-09-25T07:42:10+05:00",
          "scale" : 1.5,
          "somethingNew" : { "a" : 1 },
          "version" : 1
        }
        """;
        var edits = Decode(json);
        Assert.Equal(1.5, edits.Scale);
        Assert.Equal(DateTimeOffset.Parse("2026-09-25T02:42:10Z"), edits.Captured);
        Assert.Equal(Guid.Parse("6F1C2E7A-0D3B-4C55-9E0B-6A1D5B2F9C10"), edits.Annotations[0].Id);
        Assert.Equal(new AnnotationKind.Line(new Point(1, 2), new Point(3.5, 4)), edits.Annotations[0].Kind);
    }

    [Fact]
    public void TextIsWrittenAsItReadsNotEscaped()
    {
        // The Mac writes UTF-8 as it is, so an edits.json reads the same by hand from either app.
        var text = Fixture.Annotation(new AnnotationKind.Text(new Point(1, 2), "Größe اردو ✓"));
        var (json, _) = DocumentArchive.Encode(new Document(Fixture.Capture(10, 10), annotations: [text]), Captured);
        Assert.Contains("Größe اردو ✓", Encoding.UTF8.GetString(json));
    }

    [Fact]
    public void WritesUppercaseIdsAndUtcDatesTheMacReads()
    {
        var line = Fixture.Annotation(new AnnotationKind.Line(new Point(1, 2), new Point(3, 4)));
        var (json, _) = DocumentArchive.Encode(new Document(Fixture.Capture(10, 10, 1.5), annotations: [line]),
                                               DateTimeOffset.Parse("2026-09-25T07:42:10+05:00"));
        var root = Root(json);
        Assert.Equal("2026-09-25T02:42:10Z", Json.String(root, "captured"));
        Assert.Equal(Upper(line.Id), Json.String(root["annotations"]![0]!.AsObject(), "id"));
        Assert.Null(root["resize"]);
        Assert.Null(root["crop"]);
    }
}
