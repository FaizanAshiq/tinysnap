using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Tinysnap.Dev;
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

    [AvaloniaFact]
    public void TheEditorWarmsUpUnseenWithoutKeepingAnything()
    {
        // The first editor took twice as long as later ones while its code was compiled.
        var setup = Launch();
        setup.Controller.WarmUp();
        Dispatcher.UIThread.RunJobs();
        Assert.True(setup.Controller.EditorIsWarm);
        Assert.Empty(setup.Controller.Editors);
        Assert.Empty(setup.Library.Entries());
    }

    [AvaloniaFact]
    public async Task TheTextReaderWarmsUpOnceOnASampleOfWords()
    {
        // Cold, the first Copy Text loads the recogniser and its language data; on the Mac that
        // took 26 seconds. Reading a few words at launch pays it before anyone is waiting.
        var setup = Launch();
        var reader = (FakeTextReader)setup.Platform.Text;
        setup.Controller.WarmUp();
        await setup.Controller.TextWarming!;
        setup.Controller.WarmUp();
        Assert.Equal([(480, 80, false)], reader.Asked);
    }
}
