using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Tinysnap.App.Editing;
using Tinysnap.Core;

namespace Tinysnap.App.Tests;

public class WindowTabsTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static WindowTabStrip Strip(Window window) =>
        window.GetLogicalDescendants().OfType<WindowTabStrip>().Single();

    [AvaloniaFact]
    public void ASecondCaptureOpensAsATabInTheSameWindow()
    {
        var setup = TestServices.Launch();
        setup.Controller.CaptureFullscreen();
        setup.Controller.CaptureFullscreen();
        Dispatcher.UIThread.RunJobs();
        var (first, second) = (setup.Controller.Editors[0], setup.Controller.Editors[1]);
        Assert.Equal([first, second], setup.Controller.Tabs.Members);
        Assert.False(first.IsVisible);
        Assert.True(second.IsVisible);
        var strip = Strip(second);
        Assert.True(strip.IsVisible);
        Assert.Equal([first.Title, second.Title], strip.Tabs.Select(tab => AutomationProperties.GetName(tab)));
        Assert.Equal([false, true], strip.Tabs.Select(tab => tab.IsChecked == true));
    }

    [AvaloniaFact]
    public void ChoosingATabShowsItsCaptureInTheSamePlace()
    {
        var setup = TestServices.Launch();
        setup.Controller.CaptureFullscreen();
        setup.Controller.CaptureFullscreen();
        Dispatcher.UIThread.RunJobs();
        var (first, second) = (setup.Controller.Editors[0], setup.Controller.Editors[1]);
        second.Position = new PixelPoint(120, 80);
        Click(Strip(second).Tabs[0]);
        Assert.True(first.IsVisible);
        Assert.False(second.IsVisible);
        Assert.Equal(new PixelPoint(120, 80), first.Position);
        Assert.Equal(second.ClientSize, first.ClientSize);
        Assert.Same(first, setup.Controller.Tabs.Shown);
    }

    [AvaloniaFact]
    public void TheLibraryOpensAsATabAndAnEntryFromItOpensAsAnother()
    {
        var setup = TestServices.Launch();
        setup.Controller.CaptureFullscreen();
        var editor = setup.Controller.Editors[0];
        editor.Close();
        setup.Controller.CaptureFullscreen();
        var library = setup.Controller.ShowLibrary();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(library, setup.Controller.Tabs.Members);
        Assert.True(library.IsVisible);
        Assert.False(setup.Controller.Editors[0].IsVisible);
        Assert.True(Strip(library).IsVisible);

        setup.Controller.Open(editor.Entry!);
        Dispatcher.UIThread.RunJobs();
        var reopened = setup.Controller.Editors.Single(e => e.Entry == editor.Entry);
        Assert.Contains(reopened, setup.Controller.Tabs.Members);
        Assert.True(reopened.IsVisible);
        Assert.False(library.IsVisible);
    }

    [AvaloniaFact]
    public void ClosingTheShownTabShowsItsNeighbour()
    {
        var setup = TestServices.Launch();
        setup.Controller.CaptureFullscreen();
        setup.Controller.CaptureFullscreen();
        Dispatcher.UIThread.RunJobs();
        var (first, second) = (setup.Controller.Editors[0], setup.Controller.Editors[1]);
        second.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(first.IsVisible);
        Assert.Equal([first], setup.Controller.Tabs.Members);
        Assert.False(Strip(first).IsVisible);
    }

    [AvaloniaFact]
    public void AClosedTabsCloseButtonClosesThatCaptureOnly()
    {
        var setup = TestServices.Launch();
        setup.Controller.CaptureFullscreen();
        setup.Controller.CaptureFullscreen();
        Dispatcher.UIThread.RunJobs();
        var (first, second) = (setup.Controller.Editors[0], setup.Controller.Editors[1]);
        var close = Strip(second).CloseButtons[0];
        Assert.Equal("Close " + first.Title, AutomationProperties.GetName(close));
        Click(close);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([second], setup.Controller.Editors);
        Assert.True(second.IsVisible);
    }

    [AvaloniaFact]
    public void AloneACaptureShowsNoTabs()
    {
        var setup = TestServices.Launch();
        setup.Controller.CaptureFullscreen();
        Dispatcher.UIThread.RunJobs();
        Assert.False(Strip(setup.Controller.Editors[0]).IsVisible);
    }
}
