using Avalonia.Headless.XUnit;
using Tinysnap.Dev;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

/// <summary>Tinysnap keeps itself up to date: a newer release is downloaded quietly and installed the
/// first moment nothing of Tinysnap's is open, which restarts it, and the restarted copy says so.</summary>
public class UpdateTests
{
    [AvaloniaFact]
    public void ANewerReleaseInstallsAtOnceWhenNothingIsOpen()
    {
        var setup = Launch();
        var updates = (FakeUpdates)setup.Platform.Updates!;
        updates.Newer = "9.9.9";
        var quits = 0;
        setup.Controller.StartUpdating(() => quits++);
        setup.Time.Elapse();
        Assert.Equal(1, updates.Checks);
        Assert.True(updates.WillInstall);
        Assert.Equal(1, quits);
    }

    /// <summary>A restart would close what is open, so the update waits, downloaded once.</summary>
    [AvaloniaFact]
    public void AnUpdateWaitsUntilEveryWindowIsClosed()
    {
        var setup = Launch();
        var updates = (FakeUpdates)setup.Platform.Updates!;
        updates.Newer = "9.9.9";
        var quits = 0;
        var settings = setup.Controller.ShowSettings();
        setup.Controller.StartUpdating(() => quits++);
        setup.Time.Elapse();
        setup.Time.Elapse();
        Assert.Equal(1, updates.Checks);
        Assert.False(updates.WillInstall);
        Assert.Equal(0, quits);
        settings.Close();
        setup.Time.Elapse();
        Assert.True(updates.WillInstall);
        Assert.Equal(1, quits);
    }

    [AvaloniaFact]
    public void APinOrAnOverlayHoldsTheUpdateToo()
    {
        var setup = Launch();
        var updates = (FakeUpdates)setup.Platform.Updates!;
        updates.Newer = "9.9.9";
        var quits = 0;
        setup.Controller.CaptureArea();
        setup.Controller.StartUpdating(() => quits++);
        setup.Time.Elapse();
        Assert.Equal(0, quits);
    }

    [AvaloniaFact]
    public void WithNothingNewerItLooksAgainLater()
    {
        var setup = Launch();
        var updates = (FakeUpdates)setup.Platform.Updates!;
        var quits = 0;
        setup.Controller.StartUpdating(() => quits++);
        setup.Time.Elapse();
        setup.Time.Elapse();
        Assert.Equal(2, updates.Checks);
        Assert.False(updates.WillInstall);
        Assert.Equal(0, quits);
    }

    [AvaloniaFact]
    public void TheRestartedCopySaysItWasUpdated()
    {
        var setup = Launch();
        setup.Controller.SayUpdated("1.5.0");
        Assert.Equal("Updated to Tinysnap 1.5.0", setup.Controller.Toast!.Heading);
    }
}
