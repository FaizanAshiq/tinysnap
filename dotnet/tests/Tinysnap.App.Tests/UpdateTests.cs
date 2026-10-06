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

    /// <summary>Asked for: one look a week, which a restart, as at every login, does not hurry.</summary>
    [AvaloniaFact]
    public void TheFirstLookComesSoonAfterLaunchAndTheNextAWeekLater()
    {
        var setup = Launch();
        setup.Controller.StartUpdating(() => { });
        Assert.Equal(TimeSpan.FromSeconds(10), setup.Time.NewestDue);
        setup.Time.Elapse();
        Assert.Equal(TimeSpan.FromDays(7), setup.Time.NewestDue);
    }

    [AvaloniaFact]
    public void ALookTwoDaysAgoWaitsOutTheWeekAfterARestart()
    {
        var setup = Launch();
        var stamp = Path.Combine(Path.GetDirectoryName(setup.Controller.Preferences.Path)!, "last-update-check");
        Directory.CreateDirectory(Path.GetDirectoryName(stamp)!);
        File.WriteAllText(stamp, DateTimeOffset.UtcNow.AddDays(-2).ToString("O"));
        setup.Controller.StartUpdating(() => { });
        Assert.InRange(setup.Time.NewestDue, TimeSpan.FromDays(5) - TimeSpan.FromMinutes(1), TimeSpan.FromDays(5));
        Assert.Equal(0, ((FakeUpdates)setup.Platform.Updates!).Checks);
    }

    [AvaloniaFact]
    public void TheRestartedCopySaysItWasUpdated()
    {
        var setup = Launch();
        setup.Controller.SayUpdated("1.5.0");
        Assert.Equal("Updated to Tinysnap 1.5.0", setup.Controller.Toast!.Heading);
    }
}
