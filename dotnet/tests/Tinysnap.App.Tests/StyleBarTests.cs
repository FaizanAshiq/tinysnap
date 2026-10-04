using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
        var editor = new EditorWindow(session, DateTimeOffset.Now, TestServices.Make());
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
        var editor = new EditorWindow(session, DateTimeOffset.Now, TestServices.Make());
        editor.Show();
        var bar = editor.StyleBar;
        Assert.True(bar.IsVisible);
        Assert.NotNull(bar.ColorButton);
        Assert.Equal(5, bar.SizeChips.Count);
        Assert.Empty(bar.FillChips);
        Assert.Empty(bar.CornerChips);
        Assert.Empty(bar.AlignChips);

        editor.Canvas.Choose(Tool.Text);
        Assert.Equal(3, bar.AlignChips.Count);
        Assert.Equal(2, bar.FillChips.Count);
        Assert.NotNull(bar.BoldChip);

        editor.Canvas.Choose(Tool.Rectangle);
        Assert.Equal(2, bar.FillChips.Count);
        Assert.Equal(5, bar.CornerChips.Count);
        Assert.Equal(2, bar.DashChips.Count);
        Assert.Null(bar.BoldChip);

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

    /// <summary>An editor with "Hi" being typed in its text field.</summary>
    private static (EditorWindow Editor, EditorSession Session) Typing()
    {
        var session = new EditorSession(new Document(CanvasHost.Blank(400, 300)), Tool.Text);
        var editor = new EditorWindow(session, DateTimeOffset.Now, TestServices.Make());
        editor.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();
        var at = editor.Canvas.TranslatePoint(new Point(40, 40), editor)!.Value;
        editor.MouseDown(at, MouseButton.Left);
        editor.MouseUp(at, MouseButton.Left);
        editor.KeyTextInput("Hi");
        return (editor, session);
    }

    [AvaloniaFact]
    public void AnAlignChipSetsTheTextAndTheBoxItIsTypedIn()
    {
        var (editor, session) = Typing();
        Click(editor.StyleBar.AlignChips[1]);
        Assert.Equal(TextAlign.Center, Assert.Single(session.Display.Annotations).Style.Align);
        Assert.True(editor.StyleBar.AlignChips[1].IsChecked);
        var box = editor.Canvas.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.Equal(Avalonia.Media.TextAlignment.Center, box.TextAlignment);
    }

    [AvaloniaFact]
    public void TheBoldChipSetsTheTextAndTheBoxItIsTypedIn()
    {
        var (editor, session) = Typing();
        Click(editor.StyleBar.BoldChip!);
        Assert.True(Assert.Single(session.Display.Annotations).Style.Bold);
        Assert.True(editor.StyleBar.BoldChip!.IsChecked);
        var box = editor.Canvas.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.Equal(Avalonia.Media.FontWeight.Bold, box.FontWeight);
    }

    [AvaloniaFact]
    public void TheStepBarCountsInLettersAndMovesTheStart()
    {
        var session = new EditorSession(new Document(CanvasHost.Blank(400, 300)), Tool.Step);
        var editor = new EditorWindow(session, DateTimeOffset.Now, TestServices.Make());
        editor.Show();
        var bar = editor.StyleBar;
        Assert.Equal(2, bar.CounterChips.Count);
        Assert.Equal("From 1", bar.StartLabel!.Text);
        Click(bar.RaiseStart!);
        Click(bar.RaiseStart!);
        Assert.Equal(3, session.Display.StepStart);
        Assert.Equal("From 3", bar.StartLabel!.Text);
        Click(bar.CounterChips[1]);
        Assert.True(session.StyleFor(Tool.Step).Letters);
        editor.Canvas.Choose(Tool.Arrow);
        Assert.Empty(bar.CounterChips);
        Assert.Null(bar.StartLabel);
    }

    /// <summary>Red text on a box types in white, the colour its letters are drawn in.</summary>
    [AvaloniaFact]
    public void TextOnABoxIsTypedInLettersThatReadOnTheBox()
    {
        var (editor, session) = Typing();
        Click(editor.StyleBar.FillChips[1]);
        Assert.True(Assert.Single(session.Display.Annotations).Style.Filled);
        var box = editor.Canvas.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.Equal(Avalonia.Media.Colors.White, Assert.IsType<Avalonia.Media.SolidColorBrush>(box.Foreground).Color);
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
        Click(bar.FillChips[0]);
        Click(bar.DashChips[1]);
        Assert.Equal(new Style(Palette.Red, StyleSize.ExtraLarge, corners: CornerSize.Square, dashed: true), Selected(editor));
    }

    [AvaloniaFact]
    public void ACustomColourStreamsIntoOneUndoAndKeepsItsButton()
    {
        // Rebuilding the bar on every colour replaced the button the palette hangs from, which
        // closed it mid drag.
        var editor = WithShape(Tool.Arrow);
        var bar = editor.StyleBar;
        var button = bar.ColorButton;
        bar.CustomColor!.Color = Avalonia.Media.Color.Parse("#123456");
        bar.CustomColor.Color = Avalonia.Media.Color.Parse("#654321");
        Assert.Equal("#654321", Selected(editor).ColorHex);
        Assert.Same(button, bar.ColorButton);
        editor.Canvas.Session.Undo();
        Assert.Equal(Palette.Red, Selected(editor).ColorHex);
    }
}
