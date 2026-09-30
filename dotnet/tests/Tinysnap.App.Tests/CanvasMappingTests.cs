using Tinysnap.App.Editing;
using Tinysnap.Core;

namespace Tinysnap.App.Tests;

public class CanvasMappingTests
{
    [Fact]
    public void PixelsAndDipsRoundTripAtAnyZoomAndScale()
    {
        // A canvas grown left of and above its capture, at 150% on a 2x zoom.
        var mapping = new CanvasMapping(new Rect(-40, -20, 1000, 800), CaptureScale: 1.5, Zoom: 2);
        var dips = mapping.ToDips(Point.Zero);
        Assert.Equal(40 / 1.5 * 2, dips.X, 9);
        Assert.Equal(20 / 1.5 * 2, dips.Y, 9);
        var back = mapping.ToPixels(dips);
        Assert.Equal(0, back.X, 9);
        Assert.Equal(0, back.Y, 9);
        Assert.Equal(1000 / 1.5 * 2, mapping.Size.Width, 9);
        Assert.Equal(800 / 1.5 * 2, mapping.Size.Height, 9);
    }

    [Fact]
    public void AHandleIsSixDipsAwayAtEveryZoom()
    {
        Assert.Equal(24, new CanvasMapping(new Rect(0, 0, 100, 100), 2, Zoom: 0.5).Reach);
        Assert.Equal(3, new CanvasMapping(new Rect(0, 0, 100, 100), 2, Zoom: 4).Reach);
    }

    [Fact]
    public void ACaptureThatFitsOpensAtOneHundredPercent()
    {
        var (client, zoom) = EditorFit.Initial(new Size(400, 300), new Size(1920, 1040), toolbar: 40, minimum: new Size(900, 280));
        Assert.Equal(1, zoom);
        Assert.Equal(new Size(900, 388), client);
    }

    [Fact]
    public void ACaptureLargerThanTheScreenShrinksToFitWithAMargin()
    {
        var (client, zoom) = EditorFit.Initial(new Size(3000, 2000), new Size(1920, 1040), toolbar: 40, minimum: new Size(900, 280));
        // 90% of the height, less the toolbar and a 24 point margin each side, over 2000.
        Assert.Equal(848.0 / 2000, zoom, 9);
        Assert.Equal(3000 * zoom + 48, client.Width, 9);
        Assert.Equal(2000 * zoom + 48 + 40, client.Height, 9);
    }

    [Fact]
    public void TheEditorNeverOutgrowsAScreenNarrowerThanItsToolbar()
    {
        // A 1024 by 768 screen at 100%, less its taskbar, and a toolbar that wants 1090.
        var (client, zoom) = EditorFit.Initial(new Size(400, 150), new Size(1024, 720), toolbar: 40, minimum: new Size(1090, 280));
        Assert.Equal(1, zoom);
        Assert.Equal(new Size(1024, 280), client);
    }

    [Fact]
    public void FitIsTheLargestZoomThatShowsAllOfIt()
    {
        Assert.Equal(2, EditorFit.Fit(new Size(400, 300), new Size(848, 648)), 9);
        Assert.Equal(0.2, EditorFit.Fit(new Size(4000, 3000), new Size(848, 648)), 9);
    }

    [Fact]
    public void ZoomStepsByAQuarterWithinTenAndSixteenHundredPercent()
    {
        Assert.Equal(1.25, EditorFit.ZoomIn(1), 9);
        Assert.Equal(0.8, EditorFit.ZoomOut(1), 9);
        Assert.Equal(16, EditorFit.ZoomIn(15));
        Assert.Equal(0.1, EditorFit.ZoomOut(0.11));
    }
}
