using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
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

    /// <summary>Presses the top row and drags it to the lower half of the second, without
    /// letting go.</summary>
    private static Point DragTopRowDown(EditorWindow editor)
    {
        var top = editor.Layers.Rows[0].Item;
        var start = top.TranslatePoint(new Point(40, 16), editor)!.Value;
        var end = top.TranslatePoint(new Point(40, 16 + 32 + 10), editor)!.Value;
        editor.MouseDown(start, MouseButton.Left);
        editor.MouseMove(new Point(start.X, start.Y + 6), RawInputModifiers.LeftMouseButton);
        editor.MouseMove(end, RawInputModifiers.LeftMouseButton);
        return end;
    }

    [AvaloniaFact]
    public void ADraggedRowMovesItsShapeBeforeItIsLetGoAndLandsAsOneUndoStep()
    {
        var editor = WithTwoShapes();
        Open(editor);
        editor.UpdateLayout();
        var ids = Order(editor);
        var end = DragTopRowDown(editor);
        // Already moved, list and canvas, while the button is still down; the row stays faded.
        Assert.Equal([ids[1], ids[0]], Order(editor));
        Assert.Equal(ids[1], editor.Layers.Rows[1].Id);
        Assert.Equal(0.4, editor.Layers.Rows[1].Item.Opacity);
        editor.MouseUp(end, MouseButton.Left);
        Assert.Equal([ids[1], ids[0]], Order(editor));
        Assert.Equal(1, editor.Layers.Rows[1].Item.Opacity);
        Press(editor, Key.Z, RawInputModifiers.Control, "z");
        Assert.Equal(ids, Order(editor));
    }

    [AvaloniaFact]
    public void ARowLetGoOutsideTheListGoesBack()
    {
        var editor = WithTwoShapes();
        Open(editor);
        editor.UpdateLayout();
        var ids = Order(editor);
        var end = DragTopRowDown(editor);
        var outside = new Point(end.X - 300, end.Y);
        editor.MouseMove(outside, RawInputModifiers.LeftMouseButton);
        editor.MouseUp(outside, MouseButton.Left);
        Assert.Equal(ids, Order(editor));
        // Nothing was kept: an undo takes back the second shape drawn.
        Press(editor, Key.Z, RawInputModifiers.Control, "z");
        Assert.Equal([ids[0]], Order(editor));
    }

    [AvaloniaFact]
    public void APointerAboveTheListMovesNothingAsOnTheMac()
    {
        var editor = WithTwoShapes();
        Open(editor);
        editor.UpdateLayout();
        var ids = Order(editor);
        var end = DragTopRowDown(editor);
        editor.UpdateLayout();
        // Above the list the row would have jumped back to the top.
        editor.MouseMove(new Point(end.X, end.Y - 400), RawInputModifiers.LeftMouseButton);
        Assert.Equal([ids[1], ids[0]], Order(editor));
    }

    [AvaloniaFact]
    public void TheRowsSlideToTheirNewPlacesAndTheDraggedOneKeepsTheKeys()
    {
        var editor = WithTwoShapes();
        Open(editor);
        editor.UpdateLayout();
        var dragged = editor.Layers.Rows[0].Item;
        DragTopRowDown(editor);
        Assert.Same(dragged, editor.Layers.Rows[1].Item);
        Assert.True(dragged.IsKeyboardFocusWithin);
        // The row passed over is back in the list at the next layout, then slides up from where it was.
        editor.UpdateLayout();
        var passed = editor.Layers.Rows[0].Item;
        Assert.Contains(passed.Transitions!, t => t is TransformOperationsTransition);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        // Somewhere between where it was and its new place: how far depends on the machine's clock.
        Assert.InRange(((TransformOperations)passed.RenderTransform!).Value.M32, 0.5, 32);
    }

    [AvaloniaFact]
    public void WithReduceMotionTheRowsMoveWithoutSliding()
    {
        var editor = WithTwoShapes();
        editor.Layers.ReduceMotion = true;
        Open(editor);
        editor.UpdateLayout();
        DragTopRowDown(editor);
        editor.UpdateLayout();
        Assert.Null(editor.Layers.Rows[0].Item.Transitions);
        Assert.Null(editor.Layers.Rows[1].Item.Transitions);
    }

    [AvaloniaFact]
    public void EscWhileDraggingPutsTheRowBackAndKeepsThePanel()
    {
        var editor = WithTwoShapes();
        Open(editor);
        editor.UpdateLayout();
        var ids = Order(editor);
        DragTopRowDown(editor);
        Press(editor, Key.Escape);
        Assert.Equal(ids, Order(editor));
        Assert.True(editor.Layers.IsVisible);
        Assert.False(editor.Layers.DragGhost.IsVisible);
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
    public void ADragThatStartsOnARowsButtonMovesNothing()
    {
        var editor = WithTwoShapes();
        Open(editor);
        editor.UpdateLayout();
        var ids = Order(editor);
        var eye = editor.Layers.Rows[0].HideButton;
        var start = eye.TranslatePoint(new Point(eye.Bounds.Width / 2, eye.Bounds.Height / 2), editor)!.Value;
        var end = new Point(start.X, start.Y + 42);
        editor.MouseDown(start, MouseButton.Left);
        editor.MouseMove(new Point(start.X, start.Y + 6), RawInputModifiers.LeftMouseButton);
        editor.MouseMove(end, RawInputModifiers.LeftMouseButton);
        editor.MouseUp(end, MouseButton.Left);
        Assert.Equal(ids, Order(editor));
    }

    [AvaloniaFact]
    public void ADraggedRowLiftsAndSettlesWhenDropped()
    {
        var editor = WithTwoShapes();
        Open(editor);
        editor.UpdateLayout();
        var top = editor.Layers.Rows[0].Item;
        var start = top.TranslatePoint(new Point(40, 16), editor)!.Value;
        editor.MouseDown(start, MouseButton.Left);
        editor.MouseMove(new Point(start.X, start.Y + 20), RawInputModifiers.LeftMouseButton);
        Assert.True(editor.Layers.DragGhost.IsVisible);
        Assert.Contains(editor.Layers.Rows[0].Name, editor.Layers.DragGhost.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        editor.MouseUp(new Point(start.X, start.Y + 20), MouseButton.Left);
        Assert.False(editor.Layers.DragGhost.IsVisible);
    }

    [AvaloniaFact]
    public void ANameChangeKeepsTheRowsAsTheyAre()
    {
        var editor = Editor(Make(), Tool.Rectangle);
        DrawAt(editor, new Point(20, 20), new Point(70, 60));
        Open(editor);
        var text = Annotation.New(new AnnotationKind.Text(new Core.Point(10, 10), "A"), new Style(Palette.Red));
        var document = editor.Canvas.Session.Display with { Annotations = editor.Canvas.Session.Display.Annotations.Add(text) };
        editor.Layers.Show(document, null);
        var item = editor.Layers.Rows[0].Item;
        editor.Layers.Show(document.Replacing(text with { Kind = new AnnotationKind.Text(new Core.Point(10, 10), "AB") }), null);
        Assert.Same(item, editor.Layers.Rows[0].Item);
        Assert.Equal("AB", editor.Layers.Rows[0].Name);
        Assert.Equal("AB", AutomationProperties.GetName(item));
    }

    [AvaloniaFact]
    public void TheHandShowsOnlyOverWhatCanBeMoved()
    {
        var editor = WithTwoShapes();
        Press(editor, Key.L, RawInputModifiers.Control, "l");
        editor.Canvas.Choose(Tool.Select);
        var over = editor.Canvas.TranslatePoint(new Point(125, 20), editor)!.Value;
        editor.MouseMove(over);
        Assert.NotNull(editor.Canvas.Hovered);
        Assert.Same(Cursor.Default, editor.Canvas.Cursor);
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
    public void ALockedSelectionLeavesTheBackdropAndSizePanelsUsable()
    {
        var editor = WithTwoShapes();
        Press(editor, Key.L, RawInputModifiers.Control, "l");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(editor.StyleBar.Row.IsEnabled);
        Click(editor.BackdropButton);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(editor.StyleBar.Row.IsEnabled);
        Assert.Equal(1, editor.StyleBar.Row.Opacity);
    }

    [AvaloniaFact]
    public void TheShortcutOpensTheListWithTheKeysAndSpaceHidesTheChosenRow()
    {
        var editor = WithTwoShapes();
        Press(editor, Key.L, RawInputModifiers.Control | RawInputModifiers.Shift, "L");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(editor.Layers.List.IsKeyboardFocusWithin);
        var chosen = editor.Canvas.Session.Selection!.Value;
        Press(editor, Key.Space, RawInputModifiers.None, " ");
        Assert.True(editor.Canvas.Session.Display.Annotation(chosen)!.IsHidden);
        // The row was rebuilt; the keys stay in the list, so Esc still closes it.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(editor.Layers.List.IsKeyboardFocusWithin);
        Press(editor, Key.Escape);
        Assert.False(editor.Layers.IsVisible);
        Assert.True(editor.Canvas.IsFocused);
    }

    [AvaloniaFact]
    public void ALockedSelectionShowsItsStyleWithNothingToPress()
    {
        var editor = WithTwoShapes();
        Press(editor, Key.L, RawInputModifiers.Control, "l");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(editor.StyleBar.Row.IsEnabled);
    }

    [AvaloniaFact]
    public void ARowsBinDeletesItsShapeAndALockedRowHasNone()
    {
        var editor = WithTwoShapes();
        Open(editor);
        var ids = Order(editor);
        Assert.Equal("Delete Rectangle", AutomationProperties.GetName(editor.Layers.Rows[0].DeleteButton));
        Click(editor.Layers.Rows[0].DeleteButton);
        Assert.Equal([ids[0]], Order(editor));
        Click(editor.Layers.Rows[0].LockButton);
        Assert.False(editor.Layers.Rows[0].DeleteButton.IsEnabled);
        Click(editor.Layers.Rows[0].DeleteButton);
        Assert.Equal([ids[0]], Order(editor));
        // On the chosen row, where the other icons are white, it is dimmed: it does nothing.
        editor.Layers.Rows[0].Item.IsSelected = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var bin = editor.Layers.Rows[0].DeleteButton;
        Assert.True(bin.IsVisible);
        Assert.True(bin.GetVisualDescendants().OfType<PathIcon>().Single().Foreground is not Avalonia.Media.ISolidColorBrush { Color.A: 255 }
                    || bin.Opacity < 1);
    }
}
