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

    /// <summary>Redact finds the email among the words read, hands it over to be erased, and says how
    /// many were.</summary>
    [AvaloniaFact]
    public async Task RedactFindsTheEmailAndSaysSo()
    {
        var setup = Launch();
        var email = new TextWord("marcus@example.com", new Rect(80, 10, 140, 16));
        ((FakeTextReader)setup.Platform.Text).Words = [new TextLine([new TextWord("Email", new Rect(10, 10, 60, 16)), email])];
        IReadOnlyList<Rect> erased = [];
        await setup.Controller.RedactText(CanvasHost.Blank(400, 300).Image, RedactTarget.Emails, null, boxes =>
        {
            erased = boxes;
            return boxes.Count;
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([email.Box], erased);
        Assert.Equal("Erased 1 email", setup.Controller.Toast!.Heading);
        // Found again, under an erase that already covers it.
        await setup.Controller.RedactText(CanvasHost.Blank(400, 300).Image, RedactTarget.Emails, null, _ => 0);
        Assert.Equal("Already erased", setup.Controller.Toast!.Heading);
        await setup.Controller.RedactText(CanvasHost.Blank(400, 300).Image, RedactTarget.Phones, null, boxes => boxes.Count);
        Assert.Equal("No phone numbers found", setup.Controller.Toast!.Heading);
    }

    /// <summary>A read that failed says so, and erases nothing: "No emails found" would vouch for a
    /// capture nobody read.</summary>
    [AvaloniaFact]
    public async Task RedactWithNoReaderSaysItCouldNotRead()
    {
        var setup = Launch();
        ((FakeTextReader)setup.Platform.Text).Words = null;
        var erased = false;
        await setup.Controller.RedactText(CanvasHost.Blank(400, 300).Image, RedactTarget.Emails, null, boxes =>
        {
            erased = true;
            return boxes.Count;
        });
        Assert.Equal("Could not read text", setup.Controller.Toast!.Heading);
        Assert.False(erased);
    }

    [AvaloniaFact]
    public async Task ASlowReadSaysItIsWorkingUntilTheTextLands()
    {
        var setup = Launch();
        var held = new TaskCompletionSource<TextReading?>();
        ((FakeTextReader)setup.Platform.Text).Holding = held;
        var reading = setup.Controller.ReadAndCopy(CanvasHost.Blank(400, 300).Image, false, null);
        setup.Time.Elapse();
        Assert.Equal("Reading text…", setup.Controller.Toast!.Heading);
        Assert.True(setup.Controller.Toast.IsWorking);
        held.SetResult(new TextReading([], ["Order shipped"]));
        await reading;
        Assert.Equal("Text copied", setup.Controller.Toast!.Heading);
        Assert.False(setup.Controller.Toast.IsWorking);
    }

    [AvaloniaFact]
    public async Task AQuickReadGoesStraightToTheResult()
    {
        var setup = await Read(new TextReading([], ["Order shipped"]));
        Assert.Equal("Text copied", setup.Controller.Toast!.Heading);
        Assert.False(setup.Controller.Toast.IsWorking);
    }

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
