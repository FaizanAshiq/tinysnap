using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Tinysnap.App.Capturing;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using Tinysnap.Dev;
using static Tinysnap.App.Tests.TestServices;
using AvaloniaPoint = Avalonia.Point;
using Size = Tinysnap.Core.Size;

namespace Tinysnap.App.Tests;

/// <summary>Timers that fire only when a test says so.</summary>
internal sealed class FakeTime : TimeProvider
{
    private readonly List<FakeTimer> timers = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(callback, state, dueTime);
        timers.Add(timer);
        return timer;
    }

    /// <summary>When the timer made last fires next, or never.</summary>
    public TimeSpan NewestDue => timers[^1].Due;

    /// <summary>Fires every timer that is running, then runs what they posted.</summary>
    public void Elapse()
    {
        foreach (var timer in timers.ToList()) timer.FireIfRunning();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class FakeTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        private TimeSpan due = due;

        public TimeSpan Due => due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            due = dueTime;
            return true;
        }

        public void FireIfRunning()
        {
            if (due == Timeout.InfiniteTimeSpan) return;
            due = Timeout.InfiniteTimeSpan;
            callback(state);
        }

        public void Dispose() => due = Timeout.InfiniteTimeSpan;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public class ThumbnailTests
{
    private static (CaptureThumbnail Thumbnail, FakeTime Time) Show(EditorServices services)
    {
        var time = new FakeTime();
        var thumbnail = new CaptureThumbnail(new Document(CanvasHost.Blank(400, 300)), services, time: time);
        thumbnail.Show();
        Dispatcher.UIThread.RunJobs();
        thumbnail.UpdateLayout();
        return (thumbnail, time);
    }

    private static bool Gone(CaptureThumbnail thumbnail)
    {
        Dispatcher.UIThread.RunJobs();
        return thumbnail.IsGone;
    }

    [Fact]
    public void TheThumbnailSitsInTheBottomRightCorner()
    {
        Assert.Equal(new Size(200, 150), ThumbnailGeometry.Fit(new Size(200, 150)));
        Assert.Equal(new Size(240, 120), ThumbnailGeometry.Fit(new Size(1000, 500)));
        // 20 points in from the work area's bottom right, at 1.5 pixels a point.
        var work = new PixelRect(1920, 0, 2880, 1560);
        Assert.Equal(new PixelPoint(1920 + 2880 - 30 - 360, 1560 - 30 - 180),
                     ThumbnailGeometry.Resting(work, 1.5, new Size(240, 120)));
    }

    [AvaloniaFact]
    public void LeftAloneItCopiesItselfAndGoes()
    {
        var clipboard = new FakeClipboard();
        var (thumbnail, time) = Show(Make(clipboard));
        time.Elapse();
        Assert.True(Gone(thumbnail));
        Assert.Equal(400, clipboard.Image!.Width);
    }

    [AvaloniaFact]
    public void ATimedOutThumbnailLeavesANewerClipboardAlone()
    {
        var clipboard = new FakeClipboard();
        var (thumbnail, time) = Show(Make(clipboard));
        clipboard.SetText("copied since");
        time.Elapse();
        Assert.True(Gone(thumbnail));
        Assert.Equal("copied since", clipboard.Text);
    }

    [AvaloniaFact]
    public void HoverPausesTheTimer()
    {
        var (thumbnail, time) = Show(Make());
        thumbnail.MouseMove(new AvaloniaPoint(20, 20));
        time.Elapse();
        Assert.False(Gone(thumbnail));
        thumbnail.MouseMove(new AvaloniaPoint(-50, -50));
        time.Elapse();
        Assert.True(Gone(thumbnail));
    }

    [AvaloniaFact]
    public void AClickOpensTheEditor()
    {
        var clipboard = new FakeClipboard();
        var (thumbnail, _) = Show(Make(clipboard));
        Document? opened = null;
        thumbnail.OpenRequested += document => opened = document;
        thumbnail.MouseDown(new AvaloniaPoint(60, 60), MouseButton.Left);
        thumbnail.MouseUp(new AvaloniaPoint(60, 60), MouseButton.Left);
        Assert.NotNull(opened);
        Assert.True(Gone(thumbnail));
        Assert.Null(clipboard.Image);
    }

    [AvaloniaFact]
    public void CopySaveAndPinEachDismissIt()
    {
        var clipboard = new FakeClipboard();
        var (copying, _) = Show(Make(clipboard));
        copying.Copy();
        Assert.True(Gone(copying));
        Assert.Equal(400, clipboard.Image!.Width);

        var folder = TemporaryFolder();
        var (saving, _) = Show(Make(saveFolder: folder));
        saving.Save();
        Assert.True(Gone(saving));
        Assert.Single(Directory.GetFiles(folder));

        ExportedImage? pinned = null;
        var (pinning, _) = Show(Make(pin: (exported, _) => pinned = exported));
        pinning.Pin();
        Assert.True(Gone(pinning));
        Assert.Equal(400, pinned!.Image.Width);
    }

    [AvaloniaFact]
    public void TheHoverButtonsAreNamed()
    {
        var (thumbnail, _) = Show(Make());
        Assert.Equal(["Copy", "Save", "Pin", "Close without copying"],
                     thumbnail.Buttons.Select(b => Avalonia.Automation.AutomationProperties.GetName(b)));
    }
}
