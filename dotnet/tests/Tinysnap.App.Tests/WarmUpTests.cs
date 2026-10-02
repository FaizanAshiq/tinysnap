using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class WarmUpTests
{
    [AvaloniaFact]
    public void AWarmUpOverlayOpensUnseenUnfocusedAndClosesItself()
    {
        var setup = Launch();
        setup.Controller.WarmUp();
        var warm = setup.Controller.WarmingUp!;
        Assert.True(warm.IsVisible);
        Assert.False(warm.ShowActivated);
        Assert.True(warm.Position.X < -10000);
        // A capture meanwhile is not held up by it.
        Assert.Null(setup.Controller.Overlay);
        setup.Controller.CaptureArea();
        Assert.NotNull(setup.Controller.Overlay);

        setup.Time.Elapse();
        Dispatcher.UIThread.RunJobs();
        Assert.False(warm.IsVisible);
        Assert.Null(setup.Controller.WarmingUp);
    }

    [AvaloniaFact]
    public void WhereTheDesktopPullsWindowsIntoViewTheOverlayWarmsUpWithNoWindow()
    {
        // GNOME moves a window placed off screen back into view: a black square for a second.
        var setup = Launch(placesWindowsAsAsked: false);
        setup.Controller.WarmUp();
        Assert.Null(setup.Controller.WarmingUp);
        Assert.True(setup.Controller.IsWarm);
        setup.Controller.CaptureArea();
        Assert.NotNull(setup.Controller.Overlay);
    }
}
