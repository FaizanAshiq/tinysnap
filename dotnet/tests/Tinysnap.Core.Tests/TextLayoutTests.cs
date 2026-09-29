using SkiaSharp;

namespace Tinysnap.Core.Tests;

public class TextLayoutTests
{
    [Fact]
    public void AFallbackFontIsMadeOnceAndShared()
    {
        // Made fresh for every character, fonts piled up for the finalizer on long Urdu text.
        // Emoji are in no UI font, on either platform, so they always fall back.
        using var primary = TextLayout.Font(20);
        var first = TextLayout.Runs("\U0001F600\U0001F601", primary);
        var again = TextLayout.Runs("\U0001F600", primary);
        Assert.Single(first);
        Assert.NotSame(primary, first[0].Font);
        Assert.Same(first[0].Font, again[0].Font);
    }

    [Fact]
    public void TextBoundsGrowWithEachLineAndAnEmptyStringIsStillFindable()
    {
        var one = TextLayout.Size("Hello", 16, 2);
        var two = TextLayout.Size("Hello\nWorld", 16, 2);
        Assert.Equal(one.Height * 2, two.Height, 6);
        Assert.True(TextLayout.Size("", 16, 2).Width >= 16);
    }

    [Fact]
    public void AFontIsMadeAtItsPointSizeAndScaledNotMadeAtThePixelSize()
    {
        // Doubling the scale doubles the box exactly; a font made at the pixel size would not.
        var at1 = TextLayout.Size("Tinysnap", 20, 1);
        var at2 = TextLayout.Size("Tinysnap", 20, 2);
        Assert.Equal(at1.Width * 2, at2.Width, 6);
    }

    [Fact]
    public void UrduArabicEmojiAndCombiningMarksGetInkAndWidth()
    {
        foreach (var text in new[] { "سلام دنیا", "مرحبا", "👋 hi", "été" })
        {
            var size = TextLayout.Size(text, 16, 2);
            Assert.True(size.Width > 16, $"{text} measured {size.Width}");
            var info = new SKImageInfo(400, 100, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.White);
            TextLayout.Draw(surface.Canvas, text, new Point(10, 10), 16, 2, SKColors.Black);
            using var image = surface.Snapshot();
            var buffer = PixelBuffer.From(image)!;
            var ink = 0;
            for (var y = 0; y < 100; y++)
                for (var x = 0; x < 400; x++)
                    if (buffer.Pixel(x, y).G < 200) ink++;
            Assert.True(ink > 20, $"{text} drew {ink} dark pixels");
        }
    }
}
