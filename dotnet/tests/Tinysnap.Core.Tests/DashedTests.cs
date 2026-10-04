using System.Text.Json.Nodes;

namespace Tinysnap.Core.Tests;

/// <summary>Dashed lines, boxes and ovals: the stroke breaks into dashes with the capture showing
/// between them.</summary>
public class DashedTests
{
    private static Style Style(bool dashed) => new(Palette.Red, StyleSize.ExtraLarge, dashed: dashed);

    /// <summary>How many of the pixels along y = 50, from x = 20 to 180, show the capture's white.</summary>
    private static int Gaps(AnnotationKind kind, bool dashed)
    {
        using var image = Renderer.Render(new Document(Fixture.Capture(200, 100), annotations: [Fixture.Annotation(kind, Style(dashed))]))!;
        return Enumerable.Range(20, 161).Count(x => Fixture.IsClose(Fixture.Pixel(image, x, 50), (255, 255, 255), 40));
    }

    [Fact]
    public void ADashedLineBreaksAndASolidOneDoesNot()
    {
        var line = new AnnotationKind.Line(new Point(10, 50), new Point(190, 50));
        Assert.Equal(0, Gaps(line, dashed: false));
        Assert.True(Gaps(line, dashed: true) > 30);
    }

    [Fact]
    public void ADashedBoxEdgeBreaks()
    {
        // The box's bottom edge runs along y = 50.
        var box = new AnnotationKind.Rectangle(new Rect(10, 10, 180, 40));
        Assert.Equal(0, Gaps(box, dashed: false));
        Assert.True(Gaps(box, dashed: true) > 30);
    }

    [Fact]
    public void DashedIsSavedAndAStyleFromBeforeReadsSolid()
    {
        var dashed = Style(true);
        Assert.Equal(dashed, Core.Style.FromJson(JsonNode.Parse(dashed.ToJson().ToJsonString())));
        Assert.False(Core.Style.FromJson(JsonNode.Parse("""{"colorHex":"#FF3B30","size":"medium"}""")).Dashed);
    }

    [Fact]
    public void LinesBoxesAndOvalsCanBeDashed()
    {
        Assert.True(Tool.Line.HasDash() && Tool.Rectangle.HasDash() && Tool.Oval.HasDash());
        Assert.False(Tool.Arrow.HasDash() || Tool.Text.HasDash() || Tool.Highlighter.HasDash());
    }
}
