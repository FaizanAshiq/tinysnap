using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Tinysnap.Core;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

/// <summary>Where the overlay cannot list windows, as on Wayland, the system's own picker does it.</summary>
public class SystemPickerTests
{
    [AvaloniaFact]
    public async Task CaptureWindowUsesTheSystemsPickerWhereWindowsCannotBeListed()
    {
        var picked = new Capture(CanvasHost.Blank(300, 200).Image, 1);
        var setup = Launch(picker: () => Task.FromResult<Capture?>(picked));
        setup.Controller.Perform(HotKeyAction.Window);
        await setup.Controller.WhenPicked();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(setup.Controller.Overlay);
        Assert.Equal(300, Assert.Single(setup.Controller.Editors).Canvas.Session.Display.Capture.PixelSize.Width);
    }

    [AvaloniaFact]
    public async Task SpaceInTheOverlayHandsOverToTheSystemsPicker()
    {
        var picked = new Capture(CanvasHost.Blank(300, 200).Image, 1);
        var setup = Launch(picker: () => Task.FromResult<Capture?>(picked));
        setup.Controller.CaptureArea();
        setup.Controller.Overlay!.ToggleWindowMode();
        await setup.Controller.WhenPicked();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(setup.Controller.Overlay);
        Assert.Single(setup.Controller.Editors);
    }

    [AvaloniaFact]
    public async Task CancellingTheSystemsPickerOpensNothing()
    {
        var setup = Launch(picker: () => Task.FromResult<Capture?>(null));
        setup.Controller.Perform(HotKeyAction.Window);
        await setup.Controller.WhenPicked();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(setup.Controller.Editors);
        Assert.Empty(setup.Library.Entries());
    }

    [AvaloniaFact]
    public async Task TextAndCodesKeepTheirOwnOverlayWhereWindowsAreNotListed()
    {
        var asked = 0;
        var setup = Launch(picker: () => { asked++; return Task.FromResult<Capture?>(null); });
        setup.Controller.Perform(HotKeyAction.Text);
        Assert.NotNull(setup.Controller.Overlay);
        setup.Controller.Overlay!.ToggleWindowMode();
        await setup.Controller.WhenPicked();
        Assert.Equal(0, asked);
        Assert.True(setup.Controller.Overlay?.WindowMode);
    }
}
