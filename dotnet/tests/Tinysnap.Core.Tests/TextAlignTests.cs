using System.Text.Json.Nodes;

namespace Tinysnap.Core.Tests;

/// <summary>Text set left, centred or right: each line placed inside the box the widest line makes.</summary>
public class TextAlignTests
{
    private const string Text = "A much wider line\nab";

    [Fact]
    public void EachLineSitsLeftCentredOrRightInsideTheText()
    {
        var wide = TextLayout.Size("A much wider line", 20, 1).Width;
        var shortLine = TextLayout.Size("ab", 20, 1).Width;
        Assert.Equal([0, 0], TextLayout.LineOffsets(Text, 20, TextAlign.Left));
        var centred = TextLayout.LineOffsets(Text, 20, TextAlign.Center);
        Assert.Equal(0, centred[0]);
        Assert.Equal((wide - shortLine) / 2, centred[1], 2);
        var right = TextLayout.LineOffsets(Text, 20, TextAlign.Right);
        Assert.Equal(0, right[0]);
        Assert.Equal(wide - shortLine, right[1], 2);
    }

    /// <summary>The leftmost dark pixel of the second line, drawn with <paramref name="align"/>.</summary>
    private static int SecondLineStart(TextAlign align)
    {
        var note = Annotation.New(new AnnotationKind.Text(new Point(10, 10), Text), new Style("#000000", align: align));
        using var image = Renderer.Render(new Document(Fixture.Capture(400, 120), annotations: [note]))!;
        var box = TextLayout.Size(Text, note.PixelSize(1), 1);
        int top = (int)(10 + box.Height * 0.55), bottom = (int)(10 + box.Height * 0.95);
        for (var x = 0; x < image.Width; x++)
            for (var y = top; y < bottom; y++)
                if (Fixture.Pixel(image, x, y).R < 100) return x;
        return image.Width;
    }

    [Fact]
    public void TheRendererDrawsEachAlignment()
    {
        int left = SecondLineStart(TextAlign.Left), centred = SecondLineStart(TextAlign.Center), right = SecondLineStart(TextAlign.Right);
        Assert.True(centred > left + 20, $"{left} then {centred}");
        Assert.True(right > centred + 20, $"{centred} then {right}");
    }

    [Fact]
    public void AlignmentIsSavedAndAStyleFromBeforeReadsLeft()
    {
        var style = new Style(Palette.Red, align: TextAlign.Right);
        Assert.Equal(style, Style.FromJson(JsonNode.Parse(style.ToJson().ToJsonString())));
        Assert.Equal("right", style.ToJson()["align"]!.GetValue<string>());
        Assert.Equal(TextAlign.Left, Style.FromJson(JsonNode.Parse("""{"colorHex":"#FF3B30","size":"medium"}""")).Align);
    }
}
