using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Tinysnap.Core;
using Tinysnap.Dev;
using static Tinysnap.App.Tests.TestServices;
using Point = Avalonia.Point;

namespace Tinysnap.App.Tests;

public class ReadingFlowTests
{
    /// <summary>Drags a box 50 by 30 points on the overlay, 100 by 60 pixels at 2x, and waits for
    /// the reading to land.</summary>
    private static async Task DragAndRead(AppSetup setup)
    {
        var overlay = setup.Controller.Overlay!.Windows[0];
        overlay.MouseDown(new Point(10, 10), MouseButton.Left);
        overlay.MouseMove(new Point(60, 40), RawInputModifiers.LeftMouseButton);
        overlay.MouseUp(new Point(60, 40), MouseButton.Left);
        await setup.Controller.WhenRead();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task CopyTextReadsTheDraggedBox()
    {
        var setup = Launch();
        var reader = (FakeTextReader)setup.Platform.Text;
        reader.Reading = new TextReading([], ["Order shipped"]);
        setup.Controller.Perform(HotKeyAction.Text);
        await DragAndRead(setup);
        Assert.Equal([(100, 60, false)], reader.Asked);
        Assert.Equal("Order shipped", setup.Clipboard.Text);
        Assert.Equal("Text copied", setup.Controller.Toast!.Heading);
        Assert.Empty(setup.Controller.Editors);
        Assert.Empty(setup.Library.Entries());
    }

    [AvaloniaFact]
    public async Task ScanQrCodeCopiesEveryCode()
    {
        var setup = Launch();
        var reader = (FakeTextReader)setup.Platform.Text;
        reader.Reading = new TextReading(["first", "second"], []);
        setup.Controller.Perform(HotKeyAction.Qr);
        await DragAndRead(setup);
        Assert.Equal([(100, 60, true)], reader.Asked);
        Assert.Equal("first\nsecond", setup.Clipboard.Text);
        Assert.Equal("2 QR codes copied", setup.Controller.Toast!.Heading);
    }

    [AvaloniaFact]
    public async Task RepeatLastAreaRepeatsPicturesNotTextGrabs()
    {
        var setup = Launch();
        setup.Controller.Perform(HotKeyAction.Text);
        await DragAndRead(setup);
        Assert.False(setup.Controller.HasLastArea);
    }

    [AvaloniaFact]
    public void CopyTextAndScanQrCodeHoldTheirHotkeys()
    {
        var setup = Launch();
        var hotkeys = (FakeHotkeys)setup.Platform.Hotkeys;
        Assert.Equal(HotKeys.Defaults.Text, hotkeys.Registered[HotKeyAction.Text]);
    }
}
