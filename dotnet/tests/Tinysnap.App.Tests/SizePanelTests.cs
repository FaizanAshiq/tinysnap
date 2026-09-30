using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class SizePanelTests
{
    /// <summary>An editor on a 400 by 300 capture at 2x with its Size panel open.</summary>
    private static EditorWindow Open(ExportScale setting = ExportScale.Native)
    {
        var editor = Editor(Make() with { Preferences = () => Preferences.Defaults with { ExportScale = setting } });
        Click(editor.OutputButtons.OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Export size"));
        return editor;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Types over a field and presses Enter.</summary>
    private static void Enter(EditorWindow editor, TextBox field, string text)
    {
        field.Text = text;
        field.Focus();
        editor.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ThePanelShowsTheSizeInUse()
    {
        var editor = Open();
        var bar = editor.StyleBar;
        Assert.Equal(StyleBarMode.Size, bar.Mode);
        Assert.Equal(["Export at 25%", "Export at 50%", "Export at 100%", "Export at 200%"],
                     bar.ExportSizeChips.Select(chip => Avalonia.Automation.AutomationProperties.GetName(chip)));
        Assert.True(bar.ExportSizeChips[2].IsChecked);
        Assert.Equal(("400", "300"), (bar.WidthField!.Text, bar.HeightField!.Text));
    }

    [AvaloniaFact]
    public void TheOneXSettingShowsAsHalf()
    {
        var bar = Open(ExportScale.OneX).StyleBar;
        Assert.True(bar.ExportSizeChips[1].IsChecked);
        Assert.Equal(("200", "150"), (bar.WidthField!.Text, bar.HeightField!.Text));
    }

    [AvaloniaFact]
    public void AChipSetsTheSize()
    {
        var editor = Open();
        Click(editor.StyleBar.ExportSizeChips[1]);
        Assert.Equal(0.5, editor.Canvas.Session.Display.Resize);
        Assert.True(editor.StyleBar.ExportSizeChips[1].IsChecked);
        Assert.Equal(("200", "150"), (editor.StyleBar.WidthField!.Text, editor.StyleBar.HeightField!.Text));
    }

    [AvaloniaFact]
    public void AWidthKeepsTheShape()
    {
        var editor = Open();
        Enter(editor, editor.StyleBar.WidthField!, "200");
        Assert.Equal(0.5, editor.Canvas.Session.Display.Resize!.Value, 3);
        Assert.Equal("150", editor.StyleBar.HeightField!.Text);
        Enter(editor, editor.StyleBar.HeightField!, "600");
        Assert.Equal("800", editor.StyleBar.WidthField!.Text);
    }

    [AvaloniaFact]
    public void ASizeIsHeldToTheLimits()
    {
        var editor = Open();
        Enter(editor, editor.StyleBar.WidthField!, "99999");
        Assert.Equal(DocumentSizing.ResizeMax, editor.Canvas.Session.Display.Resize);
        Assert.Equal("1600", editor.StyleBar.WidthField!.Text);
    }

    [AvaloniaFact]
    public void AnyOtherTextPutsTheSizeBack()
    {
        var editor = Open();
        Enter(editor, editor.StyleBar.WidthField!, "wide");
        Assert.Null(editor.Canvas.Session.Display.Resize);
        Assert.Equal("400", editor.StyleBar.WidthField!.Text);
    }

    [AvaloniaFact]
    public void TheEditorRefitsWhenABackdropGrowsTheCanvas()
    {
        var editor = Editor(Make());
        editor.Width = 420;
        editor.Height = 330;
        Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();
        Assert.Equal(1, editor.Canvas.Zoom);
        editor.Canvas.SetBackdrop(Backdrop.Defaults with { Padding = BackdropPadding.Large });
        Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();
        Assert.True(editor.Canvas.Zoom < 1);
    }
}
