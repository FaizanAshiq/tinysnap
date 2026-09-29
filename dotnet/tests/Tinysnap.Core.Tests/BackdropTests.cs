using System.Diagnostics;
using SkiaSharp;

namespace Tinysnap.Core.Tests;

public class BackdropTests
{
    [Fact]
    public void SofteningAWallpaperKeepsItsSizeAndBlursItsEdges()
    {
        // Black on the left, white on the right: softened, the seam turns grey.
        var image = Fixture.CaptureImage(200, 100, paint: c => Fixture.Fill(c, new Rect(0, 0, 100, 100), Fixture.Black));
        var soft = Backdrop.Soften(image);
        Assert.NotNull(soft);
        Assert.True(soft.Width == 200 && soft.Height == 100);
        var seam = Fixture.Pixel(soft, 100, 50);
        Assert.True(seam.R > 40 && seam.R < 215);
        Assert.True(Fixture.Pixel(soft, 5, 50).R < 40);
    }

    private static SKImage Framed(Document document, ExportScale scale = ExportScale.Native) =>
        Exporter.Export(document, scale)!.Image;

    [Fact]
    public void TheExportIsTheFramedOutputAtEitherScaleAndWithACrop()
    {
        var document = new Document(Fixture.Capture(1200, 800, 2), backdrop: Backdrop.Defaults);
        var image = Framed(document);
        Assert.True(image.Width == 1392 && image.Height == 992);
        image = Framed(document, ExportScale.OneX);
        Assert.True(image.Width == 696 && image.Height == 496);
        image = Framed(document with { Crop = new Rect(0, 0, 600, 160) });
        Assert.True(image.Width == 792 && image.Height == 352);
    }

    private static int Alpha(SKImage image, int x, int y) => PixelBuffer.From(image)!.Pixel(x, y).A;

    private static Document Document(BackdropFill fill, BackdropShadow shadow = BackdropShadow.None,
                                     CornerSize corners = CornerSize.Square, Capture? capture = null) =>
        new(capture ?? Fixture.Capture(200, 100, fill: Fixture.Blue),
            backdrop: new Backdrop(fill, "#34C759", BackdropPadding.Small, corners, shadow));

    [Fact]
    public void ASolidFillPaintsThePaddingAndAClearOneLeavesItSeeThrough()
    {
        var solid = Framed(Document(BackdropFill.Solid));
        Assert.True(Fixture.IsClose(Fixture.Pixel(solid, 2, 2), (52, 199, 89)));
        // Small padding is 24 pixels at 1x: the capture starts there.
        Assert.True(Fixture.IsClose(Fixture.Pixel(solid, 30, 30), (0, 0, 255)));

        var clear = Framed(Document(BackdropFill.Clear));
        Assert.Equal(0, Alpha(clear, 2, 2));
        Assert.Equal(255, Alpha(clear, 30, 30));
    }

    [Fact]
    public void AFrameOverAKeptGroundMatchesAFreshFrameInAFractionOfTheTime()
    {
        // The canvas frames on every change, and working out the shadow each time made
        // drawing, undo and style changes lag on a full screen capture.
        var document = new Document(Fixture.Capture(3024, 1964, 2), backdrop: Backdrop.Defaults);
        FrameGround? ground = null;
        Assert.NotNull(Renderer.RenderFramed(document, 1, null, ref ground));
        var kept = ground;
        Assert.NotNull(kept);
        document = document with
        {
            Annotations = [Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(100, 100, 400, 300)))],
        };
        var overImage = Renderer.RenderFramed(document, 1, null, ref ground);
        Assert.NotNull(overImage);
        Assert.Same(kept.Image, ground!.Image);
        var freshImage = Renderer.RenderFramed(document);
        Assert.NotNull(freshImage);
        var fresh = PixelBuffer.From(freshImage)!;
        var over = PixelBuffer.From(overImage)!;
        // Padding, the new box's stroke, the capture, and the shadow under it.
        foreach (var (x, y) in new[] { (10, 10), (196, 300), (1700, 1100), (3100, 2100) })
        {
            var a = over.Pixel(x, y);
            var b = fresh.Pixel(x, y);
            Assert.True(Fixture.IsClose((a.R, a.G, a.B), (b.R, b.G, b.B), 2));
        }
        static TimeSpan Fastest(Action work) => Enumerable.Range(0, 3).Select(_ =>
        {
            var clock = Stopwatch.StartNew();
            work();
            return clock.Elapsed;
        }).Min();
        FrameGround? reused = kept;
        Assert.True(Fastest(() => Renderer.RenderFramed(document, 1, null, ref reused)) * 3
                    < Fastest(() => Renderer.RenderFramed(document)));
    }

    [Fact]
    public void ANewCropOrBackdropBuildsANewGround()
    {
        var document = new Document(Fixture.Capture(200, 100), backdrop: Backdrop.Defaults);
        FrameGround? ground = null;
        Renderer.RenderFramed(document, 1, null, ref ground);
        var first = ground!.Image;
        var firstWidth = first.Width;
        document = document with { Crop = new Rect(0, 0, 100, 50) };
        Renderer.RenderFramed(document, 1, null, ref ground);
        var second = ground!.Image;
        Assert.True(!ReferenceEquals(second, first) && second.Width < firstWidth);
        // The ground it replaces is let go at once: a full screen frame is tens of megabytes
        // outside the collector's sight, and a stream of backdrop changes piled them up.
        Assert.Equal(IntPtr.Zero, first.Handle);
        document = document with { Backdrop = document.Backdrop! with { Padding = BackdropPadding.Large } };
        Renderer.RenderFramed(document, 1, null, ref ground);
        Assert.NotSame(second, ground!.Image);
    }

    [Fact]
    public void AWindowsSeeThroughCornerShowsTheFillNotAHole()
    {
        // A window capture's corners are transparent. The ground leaves the output out, so
        // what shows there must still be the fill.
        var capture = Fixture.Capture(100, 60, paint: c =>
        {
            Fixture.Fill(c, new Rect(0, 0, 100, 60), Fixture.Blue);
            using var clear = new SKPaint { BlendMode = SKBlendMode.Clear };
            c.DrawRect(new SKRect(0, 0, 10, 10), clear);
        });
        var backdrop = Backdrop.Defaults with
        {
            Fill = BackdropFill.Solid, ColorHex = "#34C759", Shadow = BackdropShadow.None, Corners = CornerSize.Square,
        };
        var image = Renderer.RenderFramed(new Document(capture, backdrop: backdrop));
        Assert.NotNull(image);
        var padding = Renderer.FramePadding(backdrop, 1);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, padding + 3, padding + 3), (52, 199, 89)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, padding + 50, padding + 30), (0, 0, 255)));
    }

    [Fact]
    public void TheShadowDarkensThePaddingJustBelowTheCapture()
    {
        var plain = Framed(Document(BackdropFill.Solid));
        var shaded = Framed(Document(BackdropFill.Solid, BackdropShadow.Soft));
        // Two pixels under the capture's bottom edge, which sits at 24 plus 100.
        Assert.True(Fixture.IsClose(Fixture.Pixel(plain, 124, 126), (52, 199, 89)));
        Assert.True(Fixture.Pixel(shaded, 124, 126).G < 170);
    }

    [Fact]
    public void RoundedCornersShowTheFillInTheCorner()
    {
        var image = Framed(Document(BackdropFill.Solid, corners: CornerSize.Large));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 25, 25), (52, 199, 89)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 124, 74), (0, 0, 255)));
    }

    [Fact]
    public void TheGradientRunsCornerToCornerInTheCapturesColours()
    {
        var capture = Fixture.Capture(200, 100, fill: Fixture.Blue);
        var image = Framed(Document(BackdropFill.Gradient, capture: capture));
        var first = Fixture.Rgb(capture.GradientColors[0]);
        var second = Fixture.Rgb(capture.GradientColors[1]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 0, 0), first, 8));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, image.Width - 1, image.Height - 1), second, 8));
    }

    [Fact]
    public void AWallpaperFillsTheFrameAndWithoutOneTheGradientDoes()
    {
        var withWallpaper = Document(BackdropFill.Wallpaper);
        withWallpaper = withWallpaper with
        {
            Backdrop = withWallpaper.Backdrop! with
            {
                Wallpaper = new BackdropWallpaper(Guid.NewGuid(), new PastedImage(Fixture.CaptureImage(30, 20, Fixture.Black))),
            },
        };
        Assert.True(Fixture.IsClose(Fixture.Pixel(Framed(withWallpaper), 2, 2), (0, 0, 0)));

        var without = Document(BackdropFill.Wallpaper);
        var first = Fixture.Rgb(without.Capture.GradientColors[0]);
        Assert.True(Fixture.IsClose(Fixture.Pixel(Framed(without), 0, 0), first, 8));
    }

    [Fact]
    public void TheFramedOutputIsTheOutputWithThePaddingOnEverySide()
    {
        var document = new Document(Fixture.Capture(1200, 800, 2));
        Assert.Null(document.FramedRect);
        document = document with { Backdrop = Backdrop.Defaults };
        // Medium padding is 48 points, 96 pixels on a 2x capture.
        Assert.Equal(new Rect(-96, -96, 1392, 992), document.FramedRect);
        document = document with { Crop = new Rect(0, 0, 600, 160) };
        Assert.Equal(new Rect(-96, -96, 792, 352), document.FramedRect);
    }

    [Fact]
    public void TheGradientRunsFromTheCommonestColourToTheCommonestDifferentOne()
    {
        var pale = Fixture.Rgb(0.93, 0.94, 0.96);
        var blue = Fixture.Rgb(0.2, 0.4, 0.9);
        // Mostly pale page, a blue header along the top: pale wins, blue comes second.
        var capture = Fixture.Capture(300, 200, fill: pale, paint: c => Fixture.Fill(c, new Rect(0, 0, 300, 50), blue));
        var colors = capture.GradientColors.Select(Fixture.Rgb).ToArray();
        Assert.Equal(2, colors.Length);
        Assert.True(Fixture.IsClose(colors[0], (237, 240, 245), 6));
        Assert.True(Fixture.IsClose(colors[1], (51, 102, 230), 6));
    }

    [Fact]
    public void ACaptureOfOneColourGetsAShadeOfItAsTheSecond()
    {
        var slate = Fixture.Rgb(0.2, 0.3, 0.4);
        var colors = Fixture.Capture(120, 80, fill: slate).GradientColors.Select(Fixture.Rgb).ToArray();
        Assert.True(Fixture.IsClose(colors[0], (51, 77, 102), 4));
        Assert.True(colors[1].R > colors[0].R + 20 && colors[1].G > colors[0].G + 20 && colors[1].B > colors[0].B + 20);
    }

    [Fact]
    public void ACaptureSplitEvenlyBetweenTwoColoursAlwaysStartsFromTheSameOne()
    {
        // Groups of equal size came out in dictionary order, which changes per launch, so a
        // gradient could flip on reopening. Each pair is tried both ways round, and eight
        // pairs leave a coin toss no room to pass them all by luck.
        (int, int, int)[][] pairs =
        [
            [(0, 0, 255), (255, 0, 0)], [(0, 255, 0), (255, 0, 0)], [(0, 0, 0), (255, 255, 255)],
            [(0, 0, 128), (255, 255, 0)], [(0, 128, 128), (255, 128, 0)], [(128, 0, 128), (128, 255, 0)],
            [(128, 128, 128), (255, 0, 128)], [(0, 96, 0), (255, 0, 255)],
        ];
        static SKColor Color((int R, int G, int B) c) => new((byte)c.R, (byte)c.G, (byte)c.B);
        foreach (var pair in pairs)
        {
            var (lower, higher) = (pair[0], pair[1]);
            foreach (var (left, right) in new[] { (lower, higher), (higher, lower) })
            {
                // 64 pixels square, the size the sample is read at, so nothing blends.
                var capture = Fixture.Capture(64, 64, paint: c =>
                {
                    Fixture.Fill(c, new Rect(0, 0, 32, 64), Color(left));
                    Fixture.Fill(c, new Rect(32, 0, 32, 64), Color(right));
                });
                Assert.True(Fixture.IsClose(Fixture.Rgb(capture.GradientColors[0]), lower, 2));
            }
        }
    }

    [Fact]
    public void SettingABackdropIsOneUndoableStep()
    {
        var editor = new EditorSession(new Document(Fixture.Capture(100, 100)));
        editor.SetBackdrop(Backdrop.Defaults);
        Assert.Equal(Backdrop.Defaults, editor.Display.Backdrop);
        editor.Undo();
        Assert.Null(editor.Display.Backdrop);
        editor.Redo();
        Assert.Equal(Backdrop.Defaults, editor.Display.Backdrop);
    }

    [Fact]
    public void AStreamOfColourChangesUndoesAsOne()
    {
        var editor = new EditorSession(new Document(Fixture.Capture(100, 100)));
        editor.SetBackdrop(Backdrop.Defaults);
        foreach (var hex in new[] { "#111111", "#222222", "#333333" })
            editor.SetBackdrop(Backdrop.Defaults with { Fill = BackdropFill.Solid, ColorHex = hex }, merging: true);
        editor.Undo();
        Assert.Equal(Backdrop.Defaults, editor.Display.Backdrop);
    }

    [Fact]
    public void TheSettingsSaveAndEachBadValueFallsBackAlone()
    {
        var backdrop = new Backdrop(BackdropFill.Solid, "#34C759", BackdropPadding.Large, CornerSize.Square, BackdropShadow.Strong);
        Assert.Equal(backdrop, Backdrop.FromJson(Json.Parse(Json.Write(backdrop.ToJson()))));

        var bad = Backdrop.FromJson(Json.Parse("""{"fill": "plaid", "padding": "huge", "colorHex": "green", "shadow": "strong"}"""));
        Assert.Equal(new Backdrop(BackdropFill.Gradient, Backdrop.Defaults.ColorHex, BackdropPadding.Medium, CornerSize.Medium,
                                  BackdropShadow.Strong), bad);
    }

    [Fact]
    public void AWallpaperIsNeverWrittenIntoTheSettings()
    {
        var backdrop = Backdrop.Defaults with
        {
            Fill = BackdropFill.Wallpaper,
            Wallpaper = new BackdropWallpaper(Guid.NewGuid(), new PastedImage(Fixture.CaptureImage(4, 4))),
        };
        Assert.Null(Backdrop.FromJson(Json.Parse(Json.Write(backdrop.ToJson()))).Wallpaper);
    }
}
