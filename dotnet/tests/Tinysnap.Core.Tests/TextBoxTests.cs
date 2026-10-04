using SkiaSharp;

namespace Tinysnap.Core.Tests;

/// <summary>Text on a box: filled text sits on a rounded box in its colour, with its letters in
/// black or white so they read on it, which keeps a label legible over a busy capture.</summary>
public class TextBoxTests
{
    private static readonly Point Origin = new(40, 30);

    private static Annotation Note(bool filled, string color = Palette.Red) =>
        Annotation.New(new AnnotationKind.Text(Origin, "Hello"), new Style(color, StyleSize.Large, filled: filled));

    private static SKImage Render(Annotation annotation, Guid? typing = null) =>
        Renderer.Render(new Document(Fixture.Capture(240, 120, fill: Fixture.Blue), annotations: [annotation]), typing: typing)!;

    /// <summary>Pixels inside the text's own box, where the letters are.</summary>
    private static List<(int R, int G, int B)> Letters(SKImage image, Annotation annotation)
    {
        var size = TextLayout.Size("Hello", annotation.PixelSize(1), 1);
        return [.. Enumerable.Range((int)Origin.X, (int)size.Width)
            .SelectMany(x => Enumerable.Range((int)Origin.Y, (int)size.Height).Select(y => Fixture.Pixel(image, x, y)))];
    }

    [Fact]
    public void FilledTextSitsOnABoxInItsColourWithLettersThatRead()
    {
        var filled = Note(filled: true);
        using var image = Render(filled);
        var box = filled.Bounds(1);
        // The box reaches past the letters on every side.
        Assert.True(box.MinX < Origin.X - 3 && box.MinY < Origin.Y);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, (int)box.MinX + 3, (int)box.MidY), (255, 59, 48)));
        Assert.True(Letters(image, filled).Count(p => p.R > 240 && p.G > 240 && p.B > 240) > 30);
    }

    [Fact]
    public void LettersOnALightBoxAreDark()
    {
        var yellow = Note(filled: true, "#FFCC00");
        using var image = Render(yellow);
        Assert.True(Letters(image, yellow).Count(p => p.R + p.G + p.B < 150) > 30);
    }

    [Fact]
    public void PlainTextHasNoBox()
    {
        var plain = Note(filled: false);
        using var image = Render(plain);
        Assert.Equal(Origin.X, plain.Bounds(1).MinX);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, (int)Origin.X - 3, (int)Origin.Y + 10), (0, 0, 255)));
    }

    /// <summary>While it is typed the text field shows the letters, so the render draws the box
    /// alone and the field's letters land on the same box the drawn text will have.</summary>
    [Fact]
    public void TheTextBeingTypedKeepsItsBoxAndLeavesItsLettersToTheField()
    {
        var filled = Note(filled: true);
        using var image = Render(filled, typing: filled.Id);
        Assert.All(Letters(image, filled), p => Assert.True(Fixture.IsClose(p, (255, 59, 48))));
    }

    /// <summary>Text set against the capture's left edge: its box reaches past it, so the canvas
    /// grows to take the box in rather than cutting it off.</summary>
    [Fact]
    public void TheBoxCountsWhenTheCanvasGrows()
    {
        Rect Extent(bool filled)
        {
            var text = Annotation.New(new AnnotationKind.Text(new Point(0, 30), "Hello"),
                                      new Style(Palette.Red, StyleSize.Large, filled: filled));
            return new Document(Fixture.Capture(240, 120), annotations: [text]).Extent;
        }
        Assert.Equal(0, Extent(filled: false).MinX);
        Assert.True(Extent(filled: true).MinX < 0);
    }

    [Fact]
    public void TheTextFieldTypesInTheColourTheLettersAreDrawnIn()
    {
        Assert.Equal(Palette.Color(Palette.Red), TextLayout.LetterColor(new Style(Palette.Red)));
        Assert.Equal(SKColors.White, TextLayout.LetterColor(new Style(Palette.Red, filled: true)));
        Assert.Equal(SKColors.Black, TextLayout.LetterColor(new Style("#FFCC00", filled: true)));
    }
}
