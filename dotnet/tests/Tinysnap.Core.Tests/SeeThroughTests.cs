using SkiaSharp;

namespace Tinysnap.Core.Tests;

/// <summary>Boxes and ovals can be see-through, so a filled box tints what is under it instead of
/// hiding it.</summary>
public class SeeThroughTests
{
    private static SKImage Render(AnnotationKind kind, bool filled, double opacity) =>
        Renderer.Render(new Document(Fixture.Capture(100, 100), annotations:
            [Fixture.Annotation(kind, new Style(Palette.Red, StyleSize.ExtraLarge, filled: filled, opacity: opacity))]))!;

    [Fact]
    public void AHalfSeeThroughFilledBoxTintsWhatIsUnderIt()
    {
        using var image = Render(new AnnotationKind.Rectangle(new Rect(10, 10, 80, 80)), filled: true, opacity: 0.5);
        // Red over white, half and half.
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 50, 50), (255, 157, 152), 4));
    }

    [Fact]
    public void AHalfSeeThroughOvalAndOutlineAreLighterToo()
    {
        using var oval = Render(new AnnotationKind.Oval(new Rect(10, 10, 80, 80)), filled: true, opacity: 0.5);
        Assert.True(Fixture.IsClose(Fixture.Pixel(oval, 50, 50), (255, 157, 152), 4));
        using var outline = Render(new AnnotationKind.Rectangle(new Rect(10, 10, 80, 80)), filled: false, opacity: 0.5);
        Assert.True(Fixture.IsClose(Fixture.Pixel(outline, 10, 50), (255, 157, 152), 4));
    }

    [Fact]
    public void BoxesOvalsAndPastedImagesHaveOpacity()
    {
        Assert.True(Tool.Rectangle.HasOpacity() && Tool.Oval.HasOpacity() && Tool.Image.HasOpacity());
        Assert.False(Tool.Arrow.HasOpacity() || Tool.Text.HasOpacity());
        // Only a pasted image is compared against the capture with the difference blend.
        Assert.True(Tool.Image.HasOverlay());
        Assert.False(Tool.Rectangle.HasOverlay());
    }
}
