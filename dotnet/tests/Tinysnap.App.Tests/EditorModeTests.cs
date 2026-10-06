using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using Tinysnap.Dev;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

/// <summary>Two toolbars: Essential, the tools most screenshots need, and Pro, every tool grouped by
/// kind. The switch between them stays in view however narrow the window.</summary>
public class EditorModeTests
{
    private static EditorWindow Open(EditorMode mode, Action<EditorMode>? remember = null)
    {
        var folder = TemporaryFolder();
        var services = new EditorServices(new FakeClipboard(), () => Preferences.Defaults with { SaveFolder = folder, EditorMode = mode },
                                          new FakeDialogs(), RememberMode: remember);
        return Editor(services);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static List<string?> Names(IEnumerable<Control> controls) => [.. controls.Select(AutomationProperties.GetName)];

    [AvaloniaFact]
    public void EssentialShowsCopyAndSaveThenTheEverydayToolsAndUndo()
    {
        var editor = Open(EditorMode.Essential);
        Assert.Equal(ToolInfo.Essential, editor.ShownTools);
        Assert.Equal(["Copy", "Save", "Undo"], Names(editor.ShownOutputs));
        Assert.False(editor.LayersButton.IsVisible);
        Assert.Equal(EditorMode.Essential, editor.ModeSwitch.Mode);
    }

    [AvaloniaFact]
    public void ProShowsSharingThenEveryToolGroupedByKindThenReadingAndFraming()
    {
        var editor = Open(EditorMode.Pro);
        Assert.Equal(
        [
            Tool.Select, Tool.Crop,
            Tool.Arrow, Tool.Line, Tool.Rectangle, Tool.Oval, Tool.Freehand, Tool.Highlighter,
            Tool.Text, Tool.Step, Tool.Image,
            Tool.Spotlight, Tool.Magnifier, Tool.Measure,
            Tool.Blur, Tool.Pixelate, Tool.Erase,
        ], editor.ShownTools);
        Assert.Equal(5, editor.ToolGroups);
        Assert.Equal(["Copy", "Save", "Drag out", "Pin and close", "Copy Text", "Scan QR Code", "Backdrop", "Export size"],
                     Names(editor.ShownOutputs));
        Assert.True(editor.LayersButton.IsVisible);
        Assert.Equal(EditorMode.Pro, editor.ModeSwitch.Mode);
    }

    [AvaloniaFact]
    public void TheSwitchChangesTheToolbarAndIsRemembered()
    {
        EditorMode? remembered = null;
        var editor = Open(EditorMode.Essential, mode => remembered = mode);
        Click(editor.ModeSwitch.Pro);
        Assert.Equal(EditorMode.Pro, remembered);
        Assert.Equal(17, editor.ShownTools.Count);
        Click(editor.ModeSwitch.Essential);
        Assert.Equal(EditorMode.Essential, remembered);
        Assert.Equal(ToolInfo.Essential, editor.ShownTools);
    }

    /// <summary>Hidden from the toolbar, a tool is still on its key.</summary>
    [AvaloniaFact]
    public void AToolEssentialLeavesOutStillComesWithItsKey()
    {
        var editor = Open(EditorMode.Essential);
        editor.Canvas.Focus();
        editor.KeyPress(Key.P, RawInputModifiers.None, PhysicalKey.None, "p");
        editor.KeyRelease(Key.P, RawInputModifiers.None, PhysicalKey.None, "p");
        Assert.Equal(Tool.Pixelate, editor.Canvas.Session.Tool);
    }

    [AvaloniaFact]
    public void TheUndoButtonUndoes()
    {
        var editor = Open(EditorMode.Essential);
        Draw(editor);
        Assert.Single(editor.Canvas.Session.Display.Annotations);
        Click(editor.UndoButton);
        Assert.Empty(editor.Canvas.Session.Display.Annotations);
    }

    /// <summary>Asked for: the switch is always there to switch back with, while the tools scroll.</summary>
    [AvaloniaFact]
    public void TheSwitchStaysInViewOnANarrowWindow()
    {
        var editor = Open(EditorMode.Pro);
        editor.MinWidth = 0;
        editor.Width = 480;
        Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();
        var left = editor.ModeSwitch.TranslatePoint(default, editor)!.Value.X;
        Assert.InRange(left, 0, 480 - editor.ModeSwitch.Bounds.Width);
        Assert.True(editor.ModeSwitch.IsEffectivelyVisible);
    }

    /// <summary>One choice for every editor: switched in one, every open one follows, and the next
    /// capture opens the same way.</summary>
    [AvaloniaFact]
    public void SwitchingInOneEditorSwitchesEveryOne()
    {
        var setup = Launch(mode: EditorMode.Essential);
        setup.Controller.CaptureFullscreen();
        setup.Controller.CaptureFullscreen();
        Assert.Equal(2, setup.Controller.Editors.Count);
        Click(setup.Controller.Editors[0].ModeSwitch.Pro);
        Assert.Equal(EditorMode.Pro, setup.Controller.Preferences.Current.EditorMode);
        Assert.All(setup.Controller.Editors, editor => Assert.Equal(17, editor.ShownTools.Count));
    }
}
