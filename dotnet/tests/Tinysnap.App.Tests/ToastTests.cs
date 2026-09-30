using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Tinysnap.Core;
using Tinysnap.Dev;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class ToastTests
{
    /// <summary>Reads a blank capture as though <paramref name="reading"/> were what it held.</summary>
    private static async Task<AppSetup> Read(TextReading? reading, bool codes = false)
    {
        var setup = Launch();
        ((FakeTextReader)setup.Platform.Text).Reading = reading;
        await setup.Controller.ReadAndCopy(CanvasHost.Blank(400, 300).Image, codes, null);
        Dispatcher.UIThread.RunJobs();
        return setup;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task TextIsCopiedAndShown()
    {
        var setup = await Read(new TextReading([], ["Order shipped", "Arrives soon"]));
        Assert.Equal("Order shipped\nArrives soon", setup.Clipboard.Text);
        var toast = setup.Controller.Toast!;
        Assert.Equal("Text copied", toast.Heading);
        Assert.Equal("Order shipped\nArrives soon", toast.Preview);
        Assert.True(toast.JoinLines.IsVisible);
        Assert.False(toast.OpenLink.IsVisible);
    }

    [AvaloniaFact]
    public async Task ThePreviewShowsFourLinesAtMost()
    {
        var setup = await Read(new TextReading([], ["1", "2", "3", "4", "5", "6"]));
        Assert.Equal("1\n2\n3\n4", setup.Controller.Toast!.Preview);
        Assert.Equal("1\n2\n3\n4\n5\n6", setup.Clipboard.Text);
    }

    [AvaloniaFact]
    public async Task JoinLinesCopiesOneLine()
    {
        var setup = await Read(new TextReading([], ["Order shipped", "Arrives soon"]));
        Click(setup.Controller.Toast!.JoinLines);
        Assert.Equal("Order shipped Arrives soon", setup.Clipboard.Text);
        Assert.Equal("Joined and copied", setup.Controller.Toast!.Heading);
    }

    [AvaloniaFact]
    public async Task ALinkOffersOpenLink()
    {
        var setup = await Read(new TextReading(["https://tinysnap.example/qr"], []), codes: true);
        var toast = setup.Controller.Toast!;
        Assert.Equal("QR code copied", toast.Heading);
        Assert.False(toast.JoinLines.IsVisible);
        Click(toast.OpenLink);
        Assert.Equal([new Uri("https://tinysnap.example/qr")], setup.Files.Opened);
        Assert.True(toast.IsGone);
    }

    [AvaloniaFact]
    public async Task SeveralCodesAreCounted()
    {
        var setup = await Read(new TextReading(["one", "two"], []), codes: true);
        Assert.Equal("2 QR codes copied", setup.Controller.Toast!.Heading);
        Assert.Equal("one\ntwo", setup.Clipboard.Text);
    }

    [AvaloniaFact]
    public async Task NothingFoundSaysSo()
    {
        var text = await Read(new TextReading([], []));
        Assert.Equal("No text found", text.Controller.Toast!.Heading);
        Assert.Null(text.Clipboard.Text);
        var codes = await Read(new TextReading([], []), codes: true);
        Assert.Equal("No QR code found", codes.Controller.Toast!.Heading);
    }

    [AvaloniaFact]
    public async Task NoEngineSaysItCouldNotRead()
    {
        var text = await Read(null);
        Assert.Equal("Could not read text", text.Controller.Toast!.Heading);
        Assert.Null(text.Clipboard.Text);
        var codes = await Read(null, codes: true);
        Assert.Equal("Could not scan for a QR code", codes.Controller.Toast!.Heading);
    }

    [AvaloniaFact]
    public async Task TheToastGoesAfterFiveSeconds()
    {
        var setup = await Read(new TextReading([], ["Order shipped"]));
        var toast = setup.Controller.Toast!;
        setup.Time.Elapse();
        Assert.True(toast.IsGone);
    }

    [AvaloniaFact]
    public async Task ANewReadingReplacesTheToast()
    {
        var setup = await Read(new TextReading([], ["First"]));
        var first = setup.Controller.Toast!;
        await setup.Controller.ReadAndCopy(CanvasHost.Blank(40, 30).Image, false, null);
        Assert.True(first.IsGone);
        Assert.NotSame(first, setup.Controller.Toast);
    }
}
