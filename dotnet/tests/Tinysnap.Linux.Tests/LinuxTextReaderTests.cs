using SkiaSharp;
using Tinysnap.Core;

namespace Tinysnap.Linux.Tests;

/// <summary>Reads with the system's Tesseract and English, which CI installs.</summary>
public class LinuxTextReaderTests
{
    private static SKImage Page(string text, float size, int width, int height, SKColor paper, SKColor ink, float x = 40)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(paper);
        using var font = TextLayout.Font(size);
        using var paint = new SKPaint { Color = ink, IsAntialias = true };
        surface.Canvas.DrawText(text, x, (height - font.Metrics.Descent - font.Metrics.Ascent) / 2, SKTextAlign.Left, font, paint);
        return surface.Snapshot();
    }

    private static async Task<string?> Read(SKImage image, string? tessdata = null) =>
        (await new LinuxTextReader(tessdata).Read(image, codes: false))?.Text;

    [Fact]
    public async Task ALineOnAPageIsRead()
    {
        Linux.Only();
        using var page = Page("Tinysnap reads this line", 48, 900, 240, SKColors.White, SKColors.Black);
        Assert.Contains("Tinysnap reads this line", await Read(page));
    }

    [Fact]
    public async Task TheWarmUpSampleHasWordsToRead()
    {
        // A blank image would never load the recogniser; the sample must be read as words.
        Linux.Only();
        using var sample = Tinysnap.Core.TextReader.WarmUpSample();
        Assert.Equal("Tinysnap reads text", await Read(sample));
    }

    [Fact]
    public async Task SmallScreenTextIsRead()
    {
        Linux.Only();
        using var page = Page("Keyboard shortcuts and accessibility", 13, 400, 30, new SKColor(0xF6, 0xF5, 0xF4), new SKColor(0x2E, 0x34, 0x36), 8);
        Assert.Contains("Keyboard shortcuts", await Read(page));
    }

    [Fact]
    public async Task LightTextOnADarkGroundIsRead()
    {
        Linux.Only();
        using var page = Page("Dark mode terminal output", 16, 420, 40, new SKColor(0x1E, 0x1E, 0x1E), new SKColor(0xEE, 0xEE, 0xEE), 10);
        Assert.Contains("terminal output", await Read(page));
    }

    [Fact]
    public async Task TextTouchingTheEdgeKeepsItsFirstLetter()
    {
        Linux.Only();
        using var page = Page("Tinysnap edge", 24, 260, 34, SKColors.White, SKColors.Black, 0);
        Assert.StartsWith("Tinysnap", await Read(page));
    }

    [Fact]
    public async Task NoLanguageDataReadsNothing()
    {
        Linux.Only();
        using var page = Page("Tinysnap", 48, 400, 120, SKColors.White, SKColors.Black);
        Assert.Null(await Read(page, Directory.CreateTempSubdirectory().FullName));
    }

    [Fact]
    public async Task QrCodesAreStillReadWithNoLanguageData()
    {
        using var page = Page("Tinysnap", 48, 400, 120, SKColors.White, SKColors.Black);
        var reading = await new LinuxTextReader(Directory.CreateTempSubdirectory().FullName).Read(page, codes: true);
        Assert.NotNull(reading);
        Assert.Empty(reading.Codes);
    }
}
