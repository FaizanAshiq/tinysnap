using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Tinysnap.App.Capturing;
using Tinysnap.Platform;
using CoreRect = Tinysnap.Core.Rect;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class OverlayTests
{
    /// <summary>A 1600 by 1200 pixel monitor at 2x: its overlay is 800 by 600 points.</summary>
    private static readonly FrozenScreen Retina = Screens.Frozen(new CoreRect(0, 0, 1600, 1200), 2);

    private static (AreaOverlay Overlay, List<AreaResult> Results) Show(FrozenDesktop desktop)
    {
        var results = new List<AreaResult>();
        var overlay = new AreaOverlay(desktop, results.Add);
        overlay.Show();
        return (overlay, results);
    }

    [AvaloniaFact]
    public void ADragOnTheOverlayGivesThatBoxInPoints()
    {
        var (overlay, results) = Show(Screens.Desktop(Retina));
        var window = overlay.Windows[0];
        window.MouseDown(new Point(100, 100), MouseButton.Left);
        window.MouseMove(new Point(300, 250));
        window.MouseUp(new Point(300, 250), MouseButton.Left);
        var area = Assert.IsType<AreaResult.Area>(Assert.Single(results));
        Assert.Same(Retina, area.Screen);
        Assert.Equal(new CoreRect(100, 100, 200, 150), area.Points);
    }

    [AvaloniaFact]
    public void AnOverlayGoesWhereTheSystemPlacesItsMonitor()
    {
        // GNOME's picture of a 2x monitor right of another, while XWayland lays them out at 1x.
        var right = Screens.Frozen(new CoreRect(2000, 0, 2000, 1600), 2) with { Place = new CoreRect(1000, 0, 1000, 800) };
        var (overlay, _) = Show(Screens.Desktop(right));
        var window = overlay.Windows[0];
        Assert.Equal(new PixelPoint(1000, 0), window.Position);
        Assert.Equal((1000.0, 800.0), (window.Width, window.Height));
    }

    [AvaloniaFact]
    public void WhereTheDesktopKeepsWindowsBelowItsPanelTheOverlayIsFullScreen()
    {
        // GNOME put the overlay below its top bar, so the frozen screen showed 32 pixels low.
        var setup = Launch(placesWindowsAsAsked: false);
        setup.Controller.CaptureArea();
        Assert.Equal(WindowState.FullScreen, Assert.Single(setup.Controller.Overlay!.Windows).WindowState);
    }

    [AvaloniaFact]
    public void WhereWindowsGoWhereTheyArePutTheOverlayIsAPlainWindow()
    {
        var setup = Launch();
        setup.Controller.CaptureArea();
        Assert.Equal(WindowState.Normal, Assert.Single(setup.Controller.Overlay!.Windows).WindowState);
    }

    [AvaloniaFact]
    public void EscapeCancels()
    {
        var (overlay, results) = Show(Screens.Desktop(Retina));
        overlay.Windows[0].KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.IsType<AreaResult.Cancelled>(Assert.Single(results));
    }

    [AvaloniaFact]
    public void SpaceThenAClickPicksTheFrontmostWindowUnderThePointer()
    {
        // Pixels on a 2x monitor: the front window covers 200 to 600, the one behind 100 to 900.
        var front = new PickableWindow(new CoreRect(200, 200, 400, 300), "Front");
        var behind = new PickableWindow(new CoreRect(100, 100, 800, 600), "Behind");
        var desktop = new FrozenDesktop([Retina], [front, behind]);

        var (overlay, results) = Show(desktop);
        var window = overlay.Windows[0];
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        // Picked on the press, as on the Mac: the overlay is gone before the release.
        window.MouseDown(new Point(150, 150), MouseButton.Left);
        Assert.Same(front, Assert.IsType<AreaResult.PickedWindow>(Assert.Single(results)).Picked);

        (overlay, results) = Show(desktop);
        window = overlay.Windows[0];
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.MouseDown(new Point(60, 60), MouseButton.Left);
        Assert.Same(behind, Assert.IsType<AreaResult.PickedWindow>(Assert.Single(results)).Picked);
    }

    [AvaloniaFact]
    public void EachScreenGetsItsOwnOverlayAtItsOwnPlace()
    {
        var plain = Screens.Frozen(new CoreRect(1600, 0, 1920, 1080), 1);
        var (overlay, _) = Show(Screens.Desktop(Retina, plain));
        Assert.Equal(2, overlay.Windows.Count);
        Assert.Equal(new PixelPoint(1600, 0), overlay.Windows[1].Position);
        Assert.Equal(new Size(800, 600), new Size(overlay.Windows[0].Width, overlay.Windows[0].Height));
        Assert.Equal(new Size(1920, 1080), new Size(overlay.Windows[1].Width, overlay.Windows[1].Height));
    }

    [AvaloniaFact]
    public void AClickWithoutADragCapturesNothingAndTheOverlayStays()
    {
        var (overlay, results) = Show(Screens.Desktop(Retina));
        overlay.Windows[0].MouseDown(new Point(100, 100), MouseButton.Left);
        overlay.Windows[0].MouseUp(new Point(100, 100), MouseButton.Left);
        Assert.Empty(results);
        Assert.True(overlay.Windows[0].IsVisible);
    }

    [AvaloniaFact]
    public void TheFrozenScreenShowsWithEverythingButTheBoxDimmed()
    {
        var blue = Screens.Frozen(new CoreRect(0, 0, 1600, 1200), 2, SkiaSharp.SKColors.Blue);
        var (overlay, _) = Show(Screens.Desktop(blue));
        var window = overlay.Windows[0];
        window.MouseDown(new Point(100, 100), MouseButton.Left);
        window.MouseMove(new Point(300, 250));
        using var frame = Frames.Capture(window);
        Assert.Equal(255, frame.GetPixel(200, 200).Blue);
        Assert.InRange(frame.GetPixel(50, 50).Blue, 150, 180);
    }
}
