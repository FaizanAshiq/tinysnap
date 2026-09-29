using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using Point = Avalonia.Point;

namespace Tinysnap.App.Tests;

public class StyleBarTests
{
    /// <summary>An editor with one shape of <paramref name="tool"/>'s drawn and selected.</summary>
    private static EditorWindow WithShape(Tool tool)
    {
        var session = new EditorSession(new Document(CanvasHost.Blank(400, 300)), tool);
        var editor = new EditorWindow(session, DateTimeOffset.Now);
        editor.Show();
        // The editor sizes itself as it opens, which moves the canvas.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();
        var canvas = editor.Canvas;
        var from = canvas.TranslatePoint(new Point(10, 10), editor)!.Value;
        var to = canvas.TranslatePoint(new Point(60, 40), editor)!.Value;
        editor.MouseDown(from, MouseButton.Left);
        editor.MouseMove(to, RawInputModifiers.LeftMouseButton);
        editor.MouseUp(to, MouseButton.Left);
        Assert.NotNull(canvas.Session.Selection);
        return editor;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Style Selected(EditorWindow editor) => editor.Canvas.Session.SelectedAnnotation!.Style;

    [AvaloniaFact]
    public void TheStyleBarShowsOnlyWhatTheToolHas()
    {
        var session = new EditorSession(new Document(CanvasHost.Blank(400, 300)), Tool.Arrow);
        var editor = new EditorWindow(session, DateTimeOffset.Now);
        editor.Show();
        var bar = editor.StyleBar;
        Assert.True(bar.IsVisible);
        Assert.NotNull(bar.ColorButton);
        Assert.Equal(5, bar.SizeChips.Count);
        Assert.Empty(bar.FillChips);
        Assert.Empty(bar.CornerChips);
        Assert.Null(bar.DeleteChip);

        editor.Canvas.Choose(Tool.Rectangle);
        Assert.Equal(2, bar.FillChips.Count);
        Assert.Equal(5, bar.CornerChips.Count);

        editor.Canvas.Choose(Tool.Image);
        Assert.Null(bar.ColorButton);
        Assert.Equal(4, bar.OpacityChips.Count);
        Assert.NotNull(bar.DifferenceChip);

        editor.Canvas.Choose(Tool.Select);
        Assert.False(bar.IsVisible);
    }

    [AvaloniaFact]
    public void ASwatchRecoloursTheSelection()
    {
        var editor = WithShape(Tool.Arrow);
        Assert.NotNull(editor.StyleBar.DeleteChip);
        Click(editor.StyleBar.Swatches.Single(s => (string?)s.Tag == "#34C759"));
        Assert.Equal("#34C759", Selected(editor).ColorHex);
        Assert.Equal("#34C759", editor.Canvas.Session.ColorHex);
    }

    [AvaloniaFact]
    public void AHexFieldTakesOnlyAColour()
    {
        var editor = WithShape(Tool.Arrow);
        Assert.True(editor.StyleBar.TryHex("123abc"));
        Assert.Equal("#123ABC", Selected(editor).ColorHex);
        Assert.False(editor.StyleBar.TryHex("green"));
        Assert.Equal("#123ABC", Selected(editor).ColorHex);
    }

    [AvaloniaFact]
    public void SizeFillAndCornerChipsRestyleOnlyTheirPart()
    {
        var editor = WithShape(Tool.Rectangle);
        var bar = editor.StyleBar;
        Click(bar.SizeChips[4]);
        Assert.Equal(new Style(Palette.Red, StyleSize.ExtraLarge), Selected(editor));
        Click(bar.FillChips[1]);
        Assert.Equal(new Style(Palette.Red, StyleSize.ExtraLarge, filled: true), Selected(editor));
        Click(bar.CornerChips[0]);
        Assert.Equal(new Style(Palette.Red, StyleSize.ExtraLarge, filled: true, corners: CornerSize.Square), Selected(editor));
        Assert.True(bar.CornerChips[0].IsChecked);
        Click(bar.DeleteChip!);
        Assert.Empty(editor.Canvas.Session.Display.Annotations);
    }
}
