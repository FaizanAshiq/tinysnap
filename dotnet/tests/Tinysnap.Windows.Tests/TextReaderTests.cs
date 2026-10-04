using SkiaSharp;
using Tinysnap.Core;
using Windows.Media.Ocr;
using ZXing;
using ZXing.Common;

namespace Tinysnap.Windows.Tests;

/// <summary>The Mac's six text reading tests that need a recogniser, against Windows' own, and the
/// QR code tests. Run on a real Windows desktop, in CI.</summary>
public class TextReaderTests
{
    private static readonly WinRtTextReader Reader = new();

    /// <summary>Black text on white at 2x, each line at a pixel origin, 24 points high.</summary>
    private static Capture Page((string Text, SKPoint Origin)[] lines, int width = 900, int height = 400,
                                Action<SKCanvas>? paint = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.White);
        using var font = TextLayout.Font(48);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        foreach (var (text, origin) in lines)
            surface.Canvas.DrawText(text, origin.X, origin.Y - font.Metrics.Ascent, SKTextAlign.Left, font, ink);
        paint?.Invoke(surface.Canvas);
        return new Capture(surface.Snapshot(), 2);
    }

    private static SKImage QrCode(string message)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 240, Height = 240, Margin = 2 },
        };
        var data = writer.Write(message);
        return SKImage.FromPixelCopy(new SKImageInfo(data.Width, data.Height, SKColorType.Bgra8888, SKAlphaType.Premul), data.Pixels);
    }

    private static void NeedsARecogniser()
    {
        if (OcrEngine.TryCreateFromUserProfileLanguages() is null) Assert.Skip("No text recogniser for this machine's languages");
    }

    private static async Task<TextReading> Read(SKImage image, bool codes = false) =>
        await Reader.Read(image, codes) ?? throw new InvalidOperationException("could not read");

    [Fact]
    public async Task TheWarmUpSampleHasWordsToRead()
    {
        // A blank image would never wake the recogniser; the sample must be read as words.
        NeedsARecogniser();
        using var sample = Tinysnap.Core.TextReader.WarmUpSample();
        Assert.Equal(new[] { "Tinysnap reads text" }, (await Read(sample)).Lines.ToArray());
    }

    [Fact]
    public async Task ReadsLinesTopToBottom()
    {
        NeedsARecogniser();
        var capture = Page([("Order shipped today", new SKPoint(40, 60)), ("Tracking arrives soon", new SKPoint(40, 200))]);
        var reading = await Read(capture.Image);
        Assert.Equal(new[] { "Order shipped today", "Tracking arrives soon" }, reading.Lines.ToArray());
        Assert.True(reading.Codes.IsEmpty);
        Assert.Equal("Order shipped today\nTracking arrives soon", reading.Text);
    }

    [Fact]
    public async Task SideBySideTextReadsAcrossEachRowBeforeGoingDown()
    {
        NeedsARecogniser();
        var capture = Page([("Left top", new SKPoint(40, 60)), ("Right top", new SKPoint(560, 64)),
                            ("Left low", new SKPoint(40, 260)), ("Right low", new SKPoint(560, 256))]);
        Assert.Equal(new[] { "Left top", "Right top", "Left low", "Right low" }, (await Read(capture.Image)).Lines.ToArray());
    }

    /// <summary>Redact needs each word's place: an email read from a page sits where it was drawn,
    /// right of the word before it, on that line.</summary>
    [Fact]
    public async Task EachWordComesWithItsBox()
    {
        NeedsARecogniser();
        var capture = Page([("Email marcus@example.com", new SKPoint(40, 100))], 1100, 300);
        var words = Assert.Single((await Reader.Lines(capture.Image))!).Words;
        var email = Assert.Single(words, word => word.Text.Contains('@'));
        Assert.Equal("Email", words[0].Text);
        Assert.True(email.Box.MinX > words[0].Box.MaxX && email.Box.MaxX < 1100, $"{email.Box}");
        Assert.True(email.Box.MinY > 80 && email.Box.MaxY < 200, $"{email.Box}");
    }

    /// <summary>A capture past the recogniser's limit is read shrunk, and its words' boxes are scaled
    /// back to the capture's pixels, so an erase lands on the word rather than short of it.</summary>
    [Fact]
    public async Task AWordOnACapturePastTheLimitKeepsItsPlace()
    {
        NeedsARecogniser();
        // As wide as three screens side by side, past the limit.
        var width = (int)OcrEngine.MaxImageDimension + 800;
        var capture = Page([("Shipped today", new SKPoint(40, 600)), ("Order 10482", new SKPoint(width - 700, 600))], width, 1600);
        var lines = (await Reader.Lines(capture.Image))!;
        var words = lines.SelectMany(line => line.Words).ToList();
        var read = $"limit {OcrEngine.MaxImageDimension}, read: {string.Join(" | ", words.Select(word => $"{word.Text} {word.Box}"))}";
        Assert.True(words.Any(word => word.Text == "Shipped"), read);
        var number = Assert.Single(words, word => word.Text.Contains("10482"));
        Assert.True(number.Box.MinX > width - 600 && number.Box.MinX < width - 100, read);
    }

    [Fact]
    public async Task CopyingTextReadsOnlyTheTextAndScanningReadsOnlyTheCode()
    {
        NeedsARecogniser();
        const string link = "https://tinysnap.example/qr";
        using var code = QrCode(link);
        var capture = Page([("Scan to open", new SKPoint(420, 150))], 900, 420, canvas => canvas.DrawImage(code, 40, 40));
        var text = await Read(capture.Image);
        Assert.Equal(new[] { "Scan to open" }, text.Lines.ToArray());
        Assert.True(text.Codes.IsEmpty);
        var scanned = await Read(capture.Image, codes: true);
        Assert.Equal(new[] { link }, scanned.Codes.ToArray());
        Assert.True(scanned.Lines.IsEmpty);
        Assert.Equal(new Uri(link), scanned.Link);
    }

    [Fact]
    public async Task ABlankImageReadsAsNothing()
    {
        NeedsARecogniser();
        var blank = Page([], 200, 100);
        Assert.True((await Read(blank.Image)).IsEmpty);
        Assert.True((await Read(blank.Image, codes: true)).IsEmpty);
    }

    [Fact]
    public async Task TextUnderAnEraseIsNeverRead()
    {
        NeedsARecogniser();
        var capture = Page([("Public note", new SKPoint(40, 60)), ("Secret code", new SKPoint(40, 200))]);
        var erase = Annotation.New(new AnnotationKind.Erase(new Rect(20, 180, 600, 110)), Tool.Erase.DefaultStyle());
        using var image = Exporter.ReadingImage(new Document(capture, annotations: [erase]));
        Assert.NotNull(image);
        Assert.Equal(new[] { "Public note" }, (await Read(image)).Lines.ToArray());
    }

    [Fact]
    public async Task OnlyTheTextInADraggedAreaIsRead()
    {
        NeedsARecogniser();
        var capture = Page([("Public note", new SKPoint(40, 60)), ("Other line", new SKPoint(40, 200))]);
        using var image = Exporter.ReadingImage(new Document(capture), new Rect(20, 160, 600, 120));
        Assert.NotNull(image);
        Assert.Equal(new[] { "Other line" }, (await Read(image)).Lines.ToArray());
    }

    [Fact]
    public async Task EveryQrCodeIsRead()
    {
        using var first = QrCode("first code");
        using var second = QrCode("second code");
        var capture = Page([], 700, 320, canvas =>
        {
            canvas.DrawImage(first, 40, 40);
            canvas.DrawImage(second, 400, 40);
        });
        Assert.Equal(new[] { "first code", "second code" }, (await Read(capture.Image, codes: true)).Codes.Order().ToArray());
    }

    [Fact]
    public async Task EachCodeIsCopiedOnce()
    {
        using var code = QrCode("the same code");
        var capture = Page([], 700, 320, canvas =>
        {
            canvas.DrawImage(code, 40, 40);
            canvas.DrawImage(code, 400, 40);
        });
        Assert.Equal(new[] { "the same code" }, (await Read(capture.Image, codes: true)).Codes.ToArray());
    }
}
