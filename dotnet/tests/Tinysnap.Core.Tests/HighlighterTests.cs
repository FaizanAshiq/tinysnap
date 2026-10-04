using SkiaSharp;

namespace Tinysnap.Core.Tests;

/// <summary>A highlight darkens a light capture the way a marker does, and lightens a dark one,
/// where darkening left it all but invisible: dark mode screenshots are as common as light ones.</summary>
public class HighlighterTests
{
    private static readonly SKColor Dark = new(30, 32, 38);

    private static Annotation Mark(double y, string hex = "#FFCC00") =>
        Fixture.Annotation(new AnnotationKind.Highlighter(new Point(20, y), new Point(180, y)), new Style(hex));

    private static SKImage Render(Capture capture, params Annotation[] marks) =>
        Renderer.Render(new Document(capture, annotations: [.. marks]))!;

    [Fact]
    public void OnALightCaptureItDarkensLikeAMarker()
    {
        using var image = Render(Fixture.Capture(200, 100), Mark(50));
        // Yellow multiplied over white at 40%.
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 100, 50), (255, 235, 153)));
    }

    [Fact]
    public void OnADarkCaptureItLightensSoItShows()
    {
        using var image = Render(Fixture.Capture(200, 100, fill: Dark), Mark(50));
        var pixel = Fixture.Pixel(image, 100, 50);
        // Clearly lighter than the ground, and yellow rather than grey.
        Assert.True(pixel.R > 100 && pixel.G > 90 && pixel.B < pixel.G - 20);
    }

    /// <summary>Read from the screenshot under each stroke: one over the dark half lightens, one
    /// over the light half darkens.</summary>
    [Fact]
    public void EachStrokeFollowsWhatIsUnderIt()
    {
        var capture = Fixture.Capture(200, 100, paint: canvas => Fixture.Fill(canvas, new Rect(0, 0, 200, 50), Dark));
        using var image = Render(capture, Mark(25), Mark(75));
        Assert.True(Fixture.Pixel(image, 100, 25).R > 100);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 100, 75), (255, 235, 153)));
    }

    [Fact]
    public void AWhiteHighlightShowsOnADarkCapture()
    {
        using var image = Render(Fixture.Capture(200, 100, fill: Dark), Mark(50, "#FFFFFF"));
        Assert.True(Fixture.Pixel(image, 100, 50).R > 100);
    }
}

/// <summary>The highlighter can follow the pointer, for marking a word on a line that is not
/// straight or circling a patch, as well as drawing the straight stroke it always has.</summary>
public class FreehandHighlighterTests
{
    private static EditorSession Traced()
    {
        var session = new EditorSession(new Document(Fixture.Capture(120, 80)), Tool.Highlighter);
        session.Restyle(s => s with { Freehand = true });
        session.PointerDown(new Point(20, 20), reach: 4);
        session.PointerDragged(new Point(60, 60));
        session.PointerDragged(new Point(100, 20));
        session.PointerUp();
        return session;
    }

    [Fact]
    public void AFreehandHighlightFollowsThePointer()
    {
        var session = Traced();
        var mark = Assert.Single(session.Display.Annotations);
        var path = Assert.IsType<AnnotationKind.HighlighterPath>(mark.Kind);
        Assert.Equal(3, path.Points.Length);
        Assert.Equal(Tool.Highlighter, mark.Tool);
        using var image = Renderer.Render(session.Display)!;
        // The bottom of the V, which a straight stroke from end to end never reaches.
        Assert.False(Fixture.IsClose(Fixture.Pixel(image, 60, 58), (255, 255, 255)));
    }

    [Fact]
    public void APathIsSavedWithItsEndsSoAnOlderTinysnapDrawsItStraight()
    {
        var document = Traced().Display;
        var (json, _) = DocumentArchive.Encode(document, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var text = System.Text.Encoding.UTF8.GetString(json);
        Assert.Contains("\"highlighter\"", text);
        Assert.Contains("\"from\"", text);
        Assert.Contains("\"points\"", text);
        var read = DocumentArchive.Decode(json, _ => null);
        Assert.Equal(document.Annotations.Select(a => a.Kind), read.Annotations.Select(a => a.Kind));
    }

    [Fact]
    public void FreehandIsSavedAndAStyleFromBeforeDrawsStraight()
    {
        var style = new Style(Palette.Yellow, freehand: true);
        Assert.Equal(style, Style.FromJson(System.Text.Json.Nodes.JsonNode.Parse(style.ToJson().ToJsonString())));
        Assert.False(Style.FromJson(System.Text.Json.Nodes.JsonNode.Parse("""{"colorHex":"#FFCC00","size":"medium"}""")).Freehand);
        Assert.True(Tool.Highlighter.HasFreehand());
        Assert.False(Tool.Line.HasFreehand());
    }
}
