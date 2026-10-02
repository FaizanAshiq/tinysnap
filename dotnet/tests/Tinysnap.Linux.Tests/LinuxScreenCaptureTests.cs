using SkiaSharp;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.Linux.Tests;

/// <summary>Run under <c>xvfb-run</c> at 1280 by 800, with a stand-in portal for Wayland.</summary>
public class LinuxScreenCaptureTests
{
    /// <summary>A PNG as GNOME's portal would leave it, at twice the layout's size.</summary>
    private static string Shot(int width, int height)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "Screenshot from today.png");
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Teal);
        using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void OnX11EveryMonitorIsReadStraightFromX()
    {
        Linux.Only();
        using var x11 = X11Screen.TryOpen()!;
        var capture = new LinuxScreenCapture(x11, () => throw new InvalidOperationException("no portal on X11"), wayland: false);
        var screen = Assert.Single(capture.Freeze().Screens);
        Assert.Equal(new Rect(0, 0, 1280, 800), screen.Bounds);
        Assert.Equal(1280, screen.Image.Width);
        Assert.Null(capture.PickWindow);
    }

    [Fact]
    public async Task OnWaylandThePortalsPictureIsCutAndItsFileRemoved()
    {
        Linux.Only();
        var shot = Shot(2560, 1600);
        var (_, name, owner) = await FakePortal.Start(0, new Uri(shot).AbsoluteUri);
        using var _ = owner;
        using var client = await FakePortal.Client();
        using var x11 = X11Screen.TryOpen()!;
        var capture = new LinuxScreenCapture(x11, () => client, wayland: true, portal: name);
        var desktop = await Task.Run(capture.Freeze, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        var screen = Assert.Single(desktop.Screens);
        Assert.Equal(new Rect(0, 0, 2560, 1600), screen.Bounds);
        Assert.Equal(new Rect(0, 0, 1280, 800), screen.Place);
        Assert.Equal(2 * x11.Scale, screen.Scale);
        Assert.False(File.Exists(shot));
        // The pointer is unknown on Wayland, so a fullscreen capture takes the primary monitor.
        Assert.Equal(new Tinysnap.Core.Point(1280, 800), capture.PointerPosition());
    }

    [Fact]
    public async Task ARefusedScreenshotFreezesNothing()
    {
        Linux.Only();
        var (_, name, owner) = await FakePortal.Start(1);
        using var _ = owner;
        using var client = await FakePortal.Client();
        var capture = new LinuxScreenCapture(X11Screen.TryOpen(), () => client, wayland: true, portal: name);
        Assert.Empty((await Task.Run(capture.Freeze, TestContext.Current.CancellationToken)).Screens);
    }

    [Fact]
    public async Task OnWaylandWindowsArePickedInGnomesOwnTool()
    {
        Linux.Only();
        var shot = Shot(600, 400);
        var (portal, name, owner) = await FakePortal.Start(0, new Uri(shot).AbsoluteUri);
        using var _ = owner;
        using var client = await FakePortal.Client();
        var capture = new LinuxScreenCapture(X11Screen.TryOpen(), () => client, wayland: true, portal: name);
        var picked = await capture.PickWindow!.Invoke();
        Assert.Equal(600, picked!.Image.Width);
        Assert.Equal([true], portal.Interactive);
        Assert.False(File.Exists(shot));
    }
}
