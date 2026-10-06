using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Tinysnap.Core;
using Tinysnap.Dev;
using static Tinysnap.App.Tests.TestServices;
using Point = Avalonia.Point;

namespace Tinysnap.App.Tests;

public class TrayTests
{
    private static IEnumerable<string> Headers(NativeMenu menu) =>
        menu.Items.Select(item => item is NativeMenuItemSeparator ? "-" : ((NativeMenuItem)item).Header ?? "");

    private static NativeMenuItem Item(NativeMenu menu, int index) => (NativeMenuItem)menu.Items[index];

    [AvaloniaFact]
    public void TheMenuListsEveryActionWithItsHotkey()
    {
        var setup = Launch();
        var menu = Tray.Menu(setup.Controller, () => { });
        Assert.Equal(["Capture Area", "Capture Window", "Capture Fullscreen", "Capture Text", "Scan QR Code", "Repeat Last Area", "Delayed Capture",
                      "Open Library", "-", "Settings...", "Quit Tinysnap"],
                     Headers(menu));
        Assert.Equal(new KeyGesture(Key.PrintScreen), Item(menu, 0).Gesture);
        Assert.Null(Item(menu, 1).Gesture);
        Assert.Equal(new KeyGesture(Key.O, KeyModifiers.Control | KeyModifiers.Shift), Item(menu, 3).Gesture);
        // Nothing to repeat yet.
        Assert.False(Item(menu, 5).IsEnabled);
    }

    [Fact]
    public void TheIconIsDarkOnALightTaskbarAndWhiteOnADarkOne()
    {
        // A point on the top left corner mark.
        static SkiaSharp.SKColor Stroke(bool light)
        {
            using var image = Tray.Draw(light);
            using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
            return bitmap.GetPixel(4, 8);
        }
        Assert.True(Stroke(light: true).Red < 80);
        Assert.True(Stroke(light: false).Red > 200);
    }

    [AvaloniaFact]
    public void ALeftClickOnTheIconCapturesAnArea()
    {
        var setup = Launch();
        Tray.Click(setup.Controller);
        Assert.NotNull(setup.Controller.Overlay);
        Assert.Null(setup.Controller.OpenLibraryWindow);
    }

    [AvaloniaFact]
    public void AHotkeyAnotherAppHoldsIsMarkedTakenInTheMenu()
    {
        var setup = Launch();
        ((FakeHotkeys)setup.Platform.Hotkeys).HeldElsewhere.Add(HotKeys.Defaults.Fullscreen!);
        setup.Controller.Hotkeys.Apply(setup.Controller.Preferences.Current.HotKeys);
        Assert.Equal("Capture Fullscreen (taken)", Item(Tray.Menu(setup.Controller, () => { }), 2).Header);
    }

    /// <summary>A shortcut that does nothing from the start is said once, at launch; Settings marks
    /// one taken while it is being changed.</summary>
    [AvaloniaFact]
    public void AShortcutTakenAtLaunchIsToldOnce()
    {
        var setup = Launch(held: [HotKeys.Defaults.Fullscreen!]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Ctrl+Shift+1 is already in use\n" +
                      "Another app or the system holds it. Free it there, or pick another shortcut in Tinysnap Settings."],
                     setup.Dialogs.Told);
        setup.Controller.Preferences.Update(p => p with { HotKeys = p.HotKeys with { Library = HotKeys.Defaults.Fullscreen } });
        Dispatcher.UIThread.RunJobs();
        Assert.Single(setup.Dialogs.Told);
    }

    [AvaloniaFact]
    public void NothingIsToldWhenEveryShortcutIsFree()
    {
        var setup = Launch();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(setup.Dialogs.Told);
    }

    [AvaloniaFact]
    public void RepeatLastAreaTakesTheSameBoxAgain()
    {
        var setup = Launch(Preferences.Defaults with { KeepLibrary = false });
        setup.Controller.CaptureArea();
        var overlay = setup.Controller.Overlay!.Windows[0];
        overlay.MouseDown(new Point(10, 10), MouseButton.Left);
        overlay.MouseMove(new Point(60, 40), RawInputModifiers.LeftMouseButton);
        overlay.MouseUp(new Point(60, 40), MouseButton.Left);
        setup.Controller.Perform(HotKeyAction.RepeatArea);
        Assert.Null(setup.Controller.Overlay);
        Assert.Equal(2, setup.Controller.Editors.Count);
        Assert.All(setup.Controller.Editors, editor => Assert.Equal(new Size(100, 60), editor.Canvas.Session.Display.Capture.PixelSize));
        Assert.True(Item(Tray.Menu(setup.Controller, () => { }), 5).IsEnabled);
    }

    [AvaloniaFact]
    public void CaptureWindowOpensTheOverlayReadyToPickAWindow()
    {
        var setup = Launch();
        setup.Controller.Perform(HotKeyAction.Window);
        Assert.True(setup.Controller.Overlay!.WindowMode);
        setup.Controller.Overlay.Finish(new Capturing.AreaResult.Cancelled());
        setup.Controller.Perform(HotKeyAction.Area);
        Assert.False(setup.Controller.Overlay!.WindowMode);
    }

    [AvaloniaFact]
    public void RepeatWithNoLastAreaOpensTheOverlay()
    {
        var setup = Launch();
        setup.Controller.Perform(HotKeyAction.RepeatArea);
        Assert.NotNull(setup.Controller.Overlay);
    }

    [AvaloniaFact]
    public void DelayedCaptureCountsDownThenOpensTheOverlay()
    {
        var setup = Launch(Preferences.Defaults with { DelaySeconds = 2 });
        setup.Controller.Perform(HotKeyAction.Delayed);
        Assert.Equal(2, setup.Controller.SecondsLeft);
        var menu = Tray.Menu(setup.Controller, () => { });
        Assert.Equal("Capturing in 2", Item(menu, 6).Header);
        Assert.False(Item(menu, 0).IsEnabled);
        setup.Time.Elapse();
        Assert.Equal(1, setup.Controller.SecondsLeft);
        Assert.Null(setup.Controller.Overlay);
        setup.Time.Elapse();
        Assert.Null(setup.Controller.SecondsLeft);
        Assert.NotNull(setup.Controller.Overlay);
    }

    [AvaloniaFact]
    public void HotkeysRunTheirActions()
    {
        var setup = Launch();
        ((FakeHotkeys)setup.Platform.Hotkeys).Press(HotKeyAction.Library);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(setup.Controller.OpenLibraryWindow);
        ((FakeHotkeys)setup.Platform.Hotkeys).Press(HotKeyAction.Fullscreen);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(setup.Controller.Editors);
    }

    [AvaloniaFact]
    public void ReopeningShowsSettings()
    {
        var setup = Launch();
        setup.Platform.Reopen();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(setup.Controller.OpenSettings);
    }

    [AvaloniaFact]
    public void ReopeningWithCapturesOpenBringsThemForward()
    {
        var setup = Launch();
        setup.Controller.CaptureFullscreen();
        setup.Platform.Reopen();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(setup.Controller.OpenSettings);
    }

    [AvaloniaFact]
    public void TheEditorToolbarEndsWithTheLibrary()
    {
        var opened = false;
        var editor = Editor(Make() with { OpenLibrary = () => opened = true });
        Assert.Equal("Library", Avalonia.Automation.AutomationProperties.GetName(editor.LibraryButton));
        editor.LibraryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(opened);
    }
}
