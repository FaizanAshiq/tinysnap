using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class MeasureTests
{
    /// <summary>An editor with Measure in hand, its guide already seen, recording what it remembers.</summary>
    private static (EditorWindow Editor, List<MeasureSettings> Remembered) Measuring(bool guideSeen = true)
    {
        var remembered = new List<MeasureSettings>();
        var editor = Editor(Make() with { RememberMeasure = remembered.Add });
        editor.Canvas.MeasureSettings = MeasureSettings.Defaults with { GuideSeen = guideSeen };
        Press(editor, Key.D, symbol: "d");
        Dispatcher.UIThread.RunJobs();
        return (editor, remembered);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void XAndYToggleTheLines()
    {
        var (editor, remembered) = Measuring();
        Press(editor, Key.X, symbol: "x");
        Assert.False(editor.Canvas.MeasureSettings.Across);
        Press(editor, Key.Y, symbol: "y");
        Assert.True(editor.Canvas.MeasureSettings.Down);
        Assert.Equal(editor.Canvas.MeasureSettings, remembered[^1]);
        Assert.False(editor.StyleBar.AcrossChip!.IsChecked);
        Assert.True(editor.StyleBar.DownChip!.IsChecked);
    }

    [AvaloniaFact]
    public void ArrowsStepTheEdgeContrast()
    {
        var (editor, _) = Measuring();
        Press(editor, Key.Down);
        Assert.Equal(0.07, editor.Canvas.MeasureSettings.EdgeContrast, 6);
        Press(editor, Key.Up, RawInputModifiers.Shift);
        Assert.Equal(0.12, editor.Canvas.MeasureSettings.EdgeContrast, 6);
        Assert.Equal("12%", editor.StyleBar.ContrastLabel!.Text);
    }

    [AvaloniaFact]
    public void TheChipsDoWhatTheKeysDo()
    {
        var (editor, _) = Measuring();
        Click(editor.StyleBar.AcrossChip!);
        Assert.False(editor.Canvas.MeasureSettings.Across);
        Click(editor.StyleBar.LowerContrast!);
        Assert.Equal(0.07, editor.Canvas.MeasureSettings.EdgeContrast, 6);
        Click(editor.StyleBar.RaiseContrast!);
        Assert.Equal(0.08, editor.Canvas.MeasureSettings.EdgeContrast, 6);
    }

    [AvaloniaFact]
    public void TheGuideShowsOnceByItself()
    {
        var (editor, remembered) = Measuring(guideSeen: false);
        Assert.True(editor.StyleBar.GuideOpen);
        Assert.True(remembered[^1].GuideSeen);
        Click(editor.StyleBar.GuideDone!);
        Assert.False(editor.StyleBar.GuideOpen);
        Press(editor, Key.A, symbol: "a");
        Press(editor, Key.D, symbol: "d");
        Dispatcher.UIThread.RunJobs();
        Assert.False(editor.StyleBar.GuideOpen);
        Click(editor.StyleBar.HelpChip!);
        Assert.True(editor.StyleBar.GuideOpen);
    }

    [AvaloniaFact]
    public void MeasureSettingsReachEveryEditor()
    {
        var setup = Launch(Preferences.Defaults with { Measure = MeasureSettings.Defaults with { GuideSeen = true } });
        setup.Controller.CaptureFullscreen();
        setup.Controller.CaptureFullscreen();
        var (first, second) = (setup.Controller.Editors[0], setup.Controller.Editors[1]);
        first.Activate();
        first.Canvas.Focus();
        Press(first, Key.D, symbol: "d");
        Press(first, Key.X, symbol: "x");
        Assert.False(setup.Controller.Preferences.Current.Measure.Across);
        Assert.False(second.Canvas.MeasureSettings.Across);
    }

    [AvaloniaFact]
    public void ANewEditorStartsFromTheRememberedSettings()
    {
        var setup = Launch(Preferences.Defaults with { Measure = MeasureSettings.Defaults with { Down = true, EdgeContrast = 0.2 } });
        setup.Controller.CaptureFullscreen();
        var editor = Assert.Single(setup.Controller.Editors);
        Assert.True(editor.Canvas.MeasureSettings.Down);
        Assert.Equal(0.2, editor.Canvas.MeasureSettings.EdgeContrast, 6);
    }
}
