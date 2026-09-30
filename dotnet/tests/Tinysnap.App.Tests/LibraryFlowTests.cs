using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;
using Tinysnap.App.Capturing;
using Tinysnap.Core;
using Tinysnap.Dev;
using Tinysnap.Platform;
using static Tinysnap.App.Tests.TestServices;
using CorePoint = Tinysnap.Core.Point;
using CoreRect = Tinysnap.Core.Rect;

namespace Tinysnap.App.Tests;

public class LibraryFlowTests
{
    private static int Annotations(LibraryStore library, LibraryEntry entry) =>
        library.Open(entry)!.Document.Annotations.Length;

    [AvaloniaFact]
    public void ACaptureIsKeptInTheLibrary()
    {
        var setup = Launch();
        setup.Controller.CaptureFullscreen();
        var entry = Assert.Single(setup.Library.Entries());
        Assert.Equal(entry, Assert.Single(setup.Controller.Editors).Entry);
    }

    [AvaloniaFact]
    public void WithTheLibraryOffNothingIsKept()
    {
        var setup = Launch(Preferences.Defaults with { KeepLibrary = false });
        setup.Controller.CaptureFullscreen();
        Assert.Empty(setup.Library.Entries());
        Assert.Null(Assert.Single(setup.Controller.Editors).Entry);
    }

    [AvaloniaFact]
    public void EditsAreKeptASecondAfterTheLastChange()
    {
        var setup = Launch();
        setup.Controller.CaptureFullscreen();
        var editor = Assert.Single(setup.Controller.Editors);
        Draw(editor);
        Assert.Equal(0, Annotations(setup.Library, editor.Entry!));
        setup.Time.Elapse();
        Assert.Equal(1, Annotations(setup.Library, editor.Entry!));
    }

    [AvaloniaFact]
    public void AKeptCaptureClosesWithoutAsking()
    {
        var setup = Launch();
        setup.Controller.CaptureFullscreen();
        var editor = Assert.Single(setup.Controller.Editors);
        var entry = editor.Entry!;
        Draw(editor);
        editor.Close();
        Assert.Equal(0, setup.Dialogs.Asked);
        Assert.Empty(setup.Controller.Editors);
        Assert.Equal(1, Annotations(setup.Library, entry));
    }

    [AvaloniaFact]
    public void AFailedLibraryWriteStillAsksBeforeClosing()
    {
        var setup = Launch();
        setup.Controller.CaptureFullscreen();
        var editor = Assert.Single(setup.Controller.Editors);
        Directory.Delete(editor.Entry!.Folder, recursive: true);
        Draw(editor);
        editor.Close();
        Assert.Equal(1, setup.Dialogs.Asked);
        Assert.Single(setup.Controller.Editors);
    }

    [AvaloniaFact]
    public async Task ClosingRendersTheImage()
    {
        var setup = Launch();
        setup.Controller.CaptureFullscreen();
        var editor = Assert.Single(setup.Controller.Editors);
        var entry = editor.Entry!;
        Draw(editor);
        editor.Close();
        await setup.Controller.WhenRendered();
        Dispatcher.UIThread.RunJobs();
        Assert.False(setup.Library.ImageIsStale(entry));
        Assert.NotEqual(File.ReadAllBytes(entry.OriginalPath), File.ReadAllBytes(entry.ImagePath));
    }

    [AvaloniaFact]
    public void AKeptThumbnailLeavesTheClipboardAlone()
    {
        var setup = Launch(Preferences.Defaults with { AfterCapture = AfterCapture.Thumbnail });
        setup.Controller.CaptureFullscreen();
        var thumbnail = setup.Controller.Thumbnail!;
        setup.Time.Elapse();
        Assert.True(thumbnail.IsGone);
        Assert.Null(setup.Clipboard.Image);
    }

    [AvaloniaFact]
    public void OpeningAnOpenEntryBringsItsEditorForward()
    {
        var setup = Launch();
        setup.Controller.CaptureFullscreen();
        var entry = Assert.Single(setup.Controller.Editors).Entry!;
        setup.Controller.Open(entry);
        Assert.Single(setup.Controller.Editors);
    }

    [AvaloniaFact]
    public void AnEntryReopensEditable()
    {
        var setup = Launch();
        setup.Controller.CaptureFullscreen();
        var editor = Assert.Single(setup.Controller.Editors);
        var entry = editor.Entry!;
        Draw(editor);
        editor.Close();
        setup.Controller.Open(entry);
        var reopened = Assert.Single(setup.Controller.Editors);
        Assert.Equal(entry, reopened.Entry);
        Assert.Single(reopened.Canvas.Session.Display.Annotations);
    }

    [AvaloniaFact]
    public void ThePinOfAKeptCaptureReopensItsEntry()
    {
        var setup = Launch();
        setup.Controller.CaptureFullscreen();
        var editor = Assert.Single(setup.Controller.Editors);
        var entry = editor.Entry!;
        editor.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, "p");
        var pin = Assert.Single(setup.Controller.Pins);
        for (var click = 0; click < 2; click++)
        {
            pin.MouseDown(new Avalonia.Point(40, 40), MouseButton.Left);
            pin.MouseUp(new Avalonia.Point(40, 40), MouseButton.Left);
        }
        Assert.Equal(entry, Assert.Single(setup.Controller.Editors).Entry);
    }

    [AvaloniaFact]
    public async Task TheSweepSparesOpenCaptures()
    {
        var setup = Launch();
        setup.Controller.CaptureFullscreen();
        var editor = Assert.Single(setup.Controller.Editors);
        Draw(editor);
        var later = DateTimeOffset.UtcNow.AddDays(LibraryStore.KeepDays + 1);
        setup.Controller.Sweep(later);
        Assert.Single(setup.Library.Entries());
        editor.Close();
        await setup.Controller.WhenRendered();
        Dispatcher.UIThread.RunJobs();
        setup.Controller.Sweep(later);
        Assert.Empty(setup.Library.Entries());
    }
}
