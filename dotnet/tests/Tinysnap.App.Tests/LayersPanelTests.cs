using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using static Tinysnap.App.Tests.TestServices;
using Point = Avalonia.Point;

namespace Tinysnap.App.Tests;

public class LayersPanelTests
{
    /// <summary>An editor with two rectangles drawn, the second one selected.</summary>
    private static EditorWindow WithTwoShapes(EditorServices? services = null)
    {
        var editor = Editor(services ?? Make(), Tool.Rectangle);
        DrawAt(editor, new Point(20, 20), new Point(70, 60));
        DrawAt(editor, new Point(100, 20), new Point(150, 60));
        Assert.Equal(2, editor.Canvas.Session.Display.Annotations.Length);
        return editor;
    }

    private static void DrawAt(EditorWindow editor, Point start, Point end)
    {
        var from = editor.Canvas.TranslatePoint(start, editor)!.Value;
        var to = editor.Canvas.TranslatePoint(end, editor)!.Value;
        editor.MouseDown(from, MouseButton.Left);
        editor.MouseMove(to, RawInputModifiers.LeftMouseButton);
        editor.MouseUp(to, MouseButton.Left);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Open(EditorWindow editor)
    {
        if (!editor.Layers.IsVisible) Click(editor.LayersButton);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static List<Guid> Order(EditorWindow editor) => [.. editor.Canvas.Session.Display.Annotations.Select(a => a.Id)];

    [AvaloniaFact]
    public void RowsListTheShapesTopFirstWithTheirNames()
    {
        var editor = WithTwoShapes();
        Open(editor);
        var ids = Order(editor);
        Assert.Equal([ids[1], ids[0]], editor.Layers.Rows.Select(row => row.Id));
        Assert.Equal(["Rectangle", "Rectangle"], editor.Layers.Rows.Select(row => row.Name));
        Assert.Equal("Layers", AutomationProperties.GetName(editor.Layers.List));
    }

    [AvaloniaFact]
    public void ClickingARowSelectsItsShape()
    {
        var editor = WithTwoShapes();
        Open(editor);
        var bottom = Order(editor)[0];
        editor.Layers.List.SelectedItem = editor.Layers.Rows[1].Item;
        Assert.Equal(bottom, editor.Canvas.Session.Selection);
    }

    [AvaloniaFact]
    public void TheEyeHidesAShapeAndTheLockLocksOne()
    {
        var editor = WithTwoShapes();
        Open(editor);
        var top = editor.Layers.Rows[0];
        Click(top.HideButton);
        Assert.True(editor.Canvas.Session.Display.Annotation(top.Id)!.IsHidden);
        Assert.Equal("Show Rectangle", AutomationProperties.GetName(editor.Layers.Rows[0].HideButton));
        Click(editor.Layers.Rows[1].LockButton);
        Assert.True(editor.Canvas.Session.Display.Annotation(editor.Layers.Rows[1].Id)!.IsLocked);
        Assert.Equal("Rectangle, locked", AutomationProperties.GetName(editor.Layers.Rows[1].Item));
    }

    [AvaloniaFact]
    public void DroppingARowReordersAsOneUndoStep()
    {
        var editor = WithTwoShapes();
        Open(editor);
        var ids = Order(editor);
        editor.Layers.Drop(ids[1], 2);
        Assert.Equal([ids[1], ids[0]], Order(editor));
        Press(editor, Key.Z, RawInputModifiers.Control, "z");
        Assert.Equal(ids, Order(editor));
    }

    [AvaloniaFact]
    public void DraggingTheTopRowBelowTheOtherSendsItsShapeBack()
    {
        var editor = WithTwoShapes();
        Open(editor);
        editor.UpdateLayout();
        var ids = Order(editor);
        var top = editor.Layers.Rows[0].Item;
        var start = top.TranslatePoint(new Point(40, 16), editor)!.Value;
        // To the lower half of the second row: the shape lands below it.
        var end = top.TranslatePoint(new Point(40, 16 + 32 + 10), editor)!.Value;
        editor.MouseDown(start, MouseButton.Left);
        editor.MouseMove(new Point(start.X, start.Y + 6), RawInputModifiers.LeftMouseButton);
        editor.MouseMove(end, RawInputModifiers.LeftMouseButton);
        editor.MouseUp(end, MouseButton.Left);
        Assert.Equal([ids[1], ids[0]], Order(editor));
    }

    [AvaloniaFact]
    public void AClickOnARowWithoutMovingMovesNothing()
    {
        var editor = WithTwoShapes();
        Open(editor);
        editor.UpdateLayout();
        var ids = Order(editor);
        var at = editor.Layers.Rows[1].Item.TranslatePoint(new Point(40, 16), editor)!.Value;
        editor.MouseDown(at, MouseButton.Left);
        editor.MouseUp(at, MouseButton.Left);
        Assert.Equal(ids, Order(editor));
        Assert.Equal(ids[0], editor.Canvas.Session.Selection);
    }

    [AvaloniaFact]
    public void TheLayersButtonOpensAndClosesThePanelAndIsRemembered()
    {
        var remembered = new List<bool>();
        var editor = WithTwoShapes(Make() with { RememberLayers = remembered.Add });
        Assert.False(editor.Layers.IsVisible);
        Click(editor.LayersButton);
        Assert.True(editor.Layers.IsVisible);
        Click(editor.LayersButton);
        Assert.False(editor.Layers.IsVisible);
        Assert.Equal([true, false], remembered);
    }

    [AvaloniaFact]
    public void AnEditorOpensWithThePanelWhenThatWasTheLastChoice()
    {
        var services = Make();
        var editor = Editor(services with { Preferences = () => services.Preferences() with { ShowsLayers = true } });
        Assert.True(editor.Layers.IsVisible);
        Assert.Equal("Shapes you draw show here", editor.Layers.EmptyNote.Text);
        Assert.True(editor.Layers.EmptyNote.IsVisible);
    }

    [AvaloniaFact]
    public void TheShortcutsArrangeLockDuplicateAndOpenThePanel()
    {
        var editor = WithTwoShapes();
        var ids = Order(editor);
        Press(editor, Key.OemOpenBrackets, RawInputModifiers.Control, "[");
        Assert.Equal([ids[1], ids[0]], Order(editor));
        Press(editor, Key.OemCloseBrackets, RawInputModifiers.Control | RawInputModifiers.Shift, "}");
        Assert.Equal(ids, Order(editor));
        Press(editor, Key.L, RawInputModifiers.Control, "l");
        Assert.True(editor.Canvas.Session.SelectedAnnotation!.IsLocked);
        Press(editor, Key.D, RawInputModifiers.Control, "d");
        Assert.Equal(3, editor.Canvas.Session.Display.Annotations.Length);
        Press(editor, Key.L, RawInputModifiers.Control | RawInputModifiers.Shift, "L");
        Assert.True(editor.Layers.IsVisible);
    }

    [AvaloniaFact]
    public void LibraryAndLayersLiveInTheRailNotTheToolbar()
    {
        var editor = Editor(Make());
        Assert.Contains(editor.LibraryButton, editor.Rail.GetVisualDescendants());
        Assert.Contains(editor.LayersButton, editor.Rail.GetVisualDescendants());
        Assert.DoesNotContain(editor.LibraryButton, editor.Toolbar.GetVisualDescendants());
    }

    [AvaloniaFact]
    public void ALockedSelectionShowsItsStyleWithNothingToPress()
    {
        var editor = WithTwoShapes();
        Assert.NotNull(editor.StyleBar.DeleteChip);
        Press(editor, Key.L, RawInputModifiers.Control, "l");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(editor.StyleBar.DeleteChip);
        Assert.False(editor.StyleBar.Row.IsEnabled);
    }
}
