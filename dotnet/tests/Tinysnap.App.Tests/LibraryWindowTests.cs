using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Tinysnap.App.Library;
using Tinysnap.Core;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class LibraryWindowTests
{
    /// <summary>A library holding a capture from now and one from two days ago, and its window.</summary>
    private static (AppSetup Setup, LibraryWindow Window, LibraryEntry Newer, LibraryEntry Older) Open()
    {
        var setup = Launch();
        var older = setup.Library.Add(CanvasHost.Blank(400, 300), DateTimeOffset.Now.AddDays(-2));
        var newer = setup.Library.Add(CanvasHost.Blank(400, 300), DateTimeOffset.Now);
        var window = setup.Controller.ShowLibrary();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (setup, window, newer, older);
    }

    [AvaloniaFact]
    public void CapturesAreGroupedByDayNewestFirst()
    {
        var setup = Launch();
        var now = DateTimeOffset.Now;
        var first = setup.Library.Add(CanvasHost.Blank(40, 30), now.AddSeconds(-5));
        var second = setup.Library.Add(CanvasHost.Blank(40, 30), now);
        var yesterday = setup.Library.Add(CanvasHost.Blank(40, 30), now.AddDays(-1));
        var window = setup.Controller.ShowLibrary();
        Assert.Equal(["Today", "Yesterday"], window.Days.Select(day => day.Title));
        Assert.Equal([second, first], window.Days[0].Entries);
        Assert.Equal([yesterday], window.Days[1].Entries);
    }

    [Fact]
    public void TilesStretchToFillEachRow()
    {
        // 1000 wide less 24 each side is 952: four tiles nearest 230, with three 16 point gaps.
        Assert.Equal((4, 226.0), LibraryLayout.Columns(1000));
        Assert.Equal((1, 252.0), LibraryLayout.Columns(300));
        Assert.Equal(171.0, LibraryLayout.TileHeight(226));
    }

    [AvaloniaFact]
    public void EachTileIsAListItemNamedForItsCapture()
    {
        var (_, window, newer, _) = Open();
        var peer = ControlAutomationPeer.CreatePeerForElement(window.TileFor(newer)!);
        Assert.Equal(AutomationControlType.ListItem, peer.GetAutomationControlType());
        Assert.True(peer.IsControlElement());
        Assert.Equal($"Capture at {newer.Captured.ToLocalTime():T}", peer.GetName());
    }

    [AvaloniaFact]
    public void TheArrowsMoveKeyboardFocusWithTheSelection()
    {
        var (_, window, newer, older) = Open();
        window.Selected = newer;
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Assert.Equal(older, window.Selected);
        Assert.Same(window.TileFor(older), window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public void TheToolbarWaitsForASelection()
    {
        var (_, window, newer, _) = Open();
        Assert.All(window.ToolbarButtons, button => Assert.False(button.IsEnabled));
        window.Selected = newer;
        Assert.All(window.ToolbarButtons, button => Assert.True(button.IsEnabled));
        Assert.Equal(["Copy", "Save", "Edit", "Pin", $"Move to {SystemWords.Bin}"],
                     window.ToolbarButtons.Select(b => Avalonia.Automation.AutomationProperties.GetName(b)));
    }

    [AvaloniaFact]
    public void CopyExportsFromTheEdits()
    {
        var (setup, window, newer, _) = Open();
        var document = setup.Library.Open(newer)!.Document;
        setup.Library.SaveEdits(document with { Resize = 0.5 }, newer);
        window.Selected = newer;
        Press(window, Key.C, RawInputModifiers.Control, "c");
        Assert.Equal(200, setup.Clipboard.Image!.Width);
    }

    [AvaloniaFact]
    public void EnterEditsTheSelection()
    {
        var (setup, window, newer, _) = Open();
        window.Selected = newer;
        Press(window, Key.Enter);
        Assert.Equal(newer, Assert.Single(setup.Controller.Editors).Entry);
    }

    [AvaloniaFact]
    public void DeleteMovesToTheRecycleBin()
    {
        var (setup, window, newer, older) = Open();
        window.Selected = newer;
        Press(window, Key.Delete);
        Assert.Equal([newer.Folder], setup.Files.Recycled);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([older], window.Days.SelectMany(day => day.Entries));
    }

    [AvaloniaFact]
    public void AnOpenCaptureIsNotTrashed()
    {
        var (setup, window, newer, _) = Open();
        setup.Controller.Open(newer);
        // Back to the library, which the editor took the keys from.
        window.ShowInFront();
        window.Focus();
        window.Selected = newer;
        Press(window, Key.Delete);
        Assert.Empty(setup.Files.Recycled);
        Assert.Contains(setup.Dialogs.Told, told => told.Contains("open in an editor"));
    }

    [AvaloniaFact]
    public void TheSelectionSurvivesAReload()
    {
        var (setup, window, _, older) = Open();
        window.Selected = older;
        setup.Controller.CaptureFullscreen();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, window.Days.Sum(day => day.Entries.Count));
        Assert.Equal(older, window.Selected);
    }

    [AvaloniaFact]
    public void SpacePreviews()
    {
        var (_, window, newer, _) = Open();
        window.Selected = newer;
        Press(window, Key.Space, symbol: " ");
        Assert.True(window.Preview?.IsVisible);
        // Only the press: the preview is gone before a release could reach it.
        window.Preview!.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.Null(window.Preview);
    }

    [AvaloniaFact]
    public void HoverShowsCopySaveAndEdit()
    {
        var (_, window, newer, _) = Open();
        var tile = window.TileFor(newer)!;
        Assert.False(tile.IsHovered);
        var middle = tile.TranslatePoint(new Avalonia.Point(40, 40), window)!.Value;
        window.MouseMove(middle);
        Assert.True(tile.IsHovered);
        Assert.Equal(["Copy", "Save", "Edit"], tile.HoverButtons.Select(b => Avalonia.Automation.AutomationProperties.GetName(b)));
    }

    [AvaloniaFact]
    public void AClickSelectsAndADoubleClickEdits()
    {
        var (setup, window, newer, _) = Open();
        var tile = window.TileFor(newer)!;
        var point = tile.TranslatePoint(new Avalonia.Point(40, 40), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(newer, window.Selected);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(newer, Assert.Single(setup.Controller.Editors).Entry);
    }
}
