using System.Text.Json.Nodes;

namespace Tinysnap.Core.Tests;

/// <summary>Bold text: drawn, measured and typed in the bold face, so its box fits it.</summary>
public class BoldTextTests
{
    private static Annotation Note(bool bold) =>
        Annotation.New(new AnnotationKind.Text(new Point(10, 10), "Weekly summary"), new Style("#000000", StyleSize.Large, bold: bold));

    [Fact]
    public void BoldTextIsWiderAndItsBoundsFitIt() =>
        Assert.True(Note(bold: true).Bounds(1).Width > Note(bold: false).Bounds(1).Width + 3);

    [Fact]
    public void BoldTextDrawsHeavierLetters()
    {
        int Ink(bool bold)
        {
            using var image = Renderer.Render(new Document(Fixture.Capture(300, 60), annotations: [Note(bold)]))!;
            return Enumerable.Range(0, image.Width).Sum(x => Enumerable.Range(0, image.Height).Count(y => Fixture.Pixel(image, x, y).R < 100));
        }
        Assert.True(Ink(true) > Ink(false) * 5 / 4);
    }

    [Fact]
    public void BoldIsSavedAndAStyleFromBeforeReadsRegular()
    {
        var style = new Style(Palette.Red, bold: true);
        Assert.Equal(style, Style.FromJson(JsonNode.Parse(style.ToJson().ToJsonString())));
        Assert.False(Style.FromJson(JsonNode.Parse("""{"colorHex":"#FF3B30","size":"medium"}""")).Bold);
    }

    [Fact]
    public void OnlyTextHasBold()
    {
        Assert.True(Tool.Text.HasBold());
        Assert.False(Tool.Arrow.HasBold() || Tool.Step.HasBold());
    }
}
