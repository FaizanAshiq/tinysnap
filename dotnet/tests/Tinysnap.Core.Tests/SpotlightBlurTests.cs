using System.Text.Json.Nodes;
using SkiaSharp;

namespace Tinysnap.Core.Tests;

/// <summary>The spotlight can blur what is outside it instead of dimming it: the rest of the
/// capture stays in its own colours, just out of focus.</summary>
public class SpotlightBlurTests
{
    /// <summary>Black and white columns two pixels wide, sharp everywhere.</summary>
    private static Capture Stripes() => Fixture.Capture(200, 100, paint: canvas =>
    {
        for (var x = 0; x < 200; x += 4) Fixture.Fill(canvas, new Rect(x, 0, 2, 100), Fixture.Black);
    });

    private static Annotation Spot(bool blur, double x = 120) =>
        Fixture.Annotation(new AnnotationKind.Spotlight(new Rect(x, 20, 60, 60)),
                           new Style(Palette.Red, corners: CornerSize.Square, blurOutside: blur));

    [Fact]
    public void OutsideABlurringSpotlightGoesSoftAndInsideStaysSharp()
    {
        using var image = Renderer.Render(new Document(Stripes(), annotations: [Spot(blur: true)]))!;
        // Outside: the columns run together into grey, neither black nor white.
        Assert.All(Enumerable.Range(20, 41), x => Assert.InRange(Fixture.Pixel(image, x, 50).R, 61, 199));
        // Inside: still black and white.
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 140, 50), (0, 0, 0)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 142, 50), (255, 255, 255)));
    }

    /// <summary>Where two spotlights overlap, the overlap is lit, blurring or dimming. Clipped by one
    /// even-odd path round them all, an overlap counted twice came out blurred.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhereTwoSpotlightsOverlapItStaysLit(bool blur)
    {
        using var image = Renderer.Render(new Document(Stripes(), annotations: [Spot(blur, 40), Spot(blur, 70)]))!;
        // In the overlap, still black and white.
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 80, 50), (0, 0, 0)), $"{Fixture.Pixel(image, 80, 50)}");
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 82, 50), (255, 255, 255)), $"{Fixture.Pixel(image, 82, 50)}");
        // In each one alone too.
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 50, 50), (255, 255, 255)), $"{Fixture.Pixel(image, 50, 50)}");
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 122, 50), (255, 255, 255)), $"{Fixture.Pixel(image, 122, 50)}");
    }

    [Fact]
    public void ADimmingSpotlightIsAsItWas()
    {
        using var image = Renderer.Render(new Document(Stripes(), annotations: [Spot(blur: false)]))!;
        // White outside, at half: still a sharp column, only darker.
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 22, 50), (128, 128, 128)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 20, 50), (0, 0, 0)));
    }

    /// <summary>Past 100% the canvas draws what is in view again; the blur there matches the whole.</summary>
    [Fact]
    public void ACloseUpOfTheBlurMatchesTheWhole()
    {
        var document = new Document(Stripes(), annotations: [Spot(blur: true)]);
        // Crisp, as the close-up draws the capture, so only the blur is compared.
        using var whole = Renderer.Render(document, outputScale: 2, sharpPixels: true)!;
        var close = Renderer.RenderCloseUp(document, new Rect(20, 20, 40, 40), 2, framed: false)!.Value;
        using var image = close.Image;
        // A white column 3 pixels inside the view, where a blur cut off at the view would read
        // the edge instead of what lies past it.
        var a = Fixture.Pixel(image, (int)((23 - close.Region.MinX) * 2), (int)((40 - close.Region.MinY) * 2));
        var b = Fixture.Pixel(whole, 46, 80);
        Assert.InRange(Math.Abs(a.R - b.R), 0, 3);
    }

    /// <summary>Every spotlight lights one shared area, so dimming or blurring one sets them all;
    /// otherwise picking Blur on any but the first did nothing.</summary>
    [Fact]
    public void BlurringOneSpotlightBlursThemAll()
    {
        var session = new EditorSession(new Document(Stripes(), annotations: [Spot(false, 10), Spot(false, 100)]), Tool.Select);
        session.PointerDown(new Point(130, 50), reach: 4);
        session.PointerUp();
        session.Restyle(s => s with { BlurOutside = true });
        Assert.All(session.Display.Annotations, a => Assert.True(a.Style.BlurOutside));
        session.Undo();
        Assert.All(session.Display.Annotations, a => Assert.False(a.Style.BlurOutside));
    }

    [Fact]
    public void BlurOutsideIsSavedAndAStyleFromBeforeDims()
    {
        var style = new Style(Palette.Red, blurOutside: true);
        Assert.Equal(style, Style.FromJson(JsonNode.Parse(style.ToJson().ToJsonString())));
        Assert.False(Style.FromJson(JsonNode.Parse("""{"colorHex":"#FF3B30","size":"medium"}""")).BlurOutside);
        Assert.True(Tool.Spotlight.HasOutsideBlur());
        Assert.False(Tool.Blur.HasOutsideBlur());
    }
}
