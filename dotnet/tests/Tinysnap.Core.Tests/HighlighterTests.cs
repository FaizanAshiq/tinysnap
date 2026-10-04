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
