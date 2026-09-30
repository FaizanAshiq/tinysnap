using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using Point = Avalonia.Point;

namespace Tinysnap.App.Tests;

public class EditorWindowTests
{
    private static EditorWindow Open(Tool tool = Tool.Arrow)
    {
        var session = new EditorSession(new Document(CanvasHost.Blank(400, 300)), tool);
        var editor = new EditorWindow(session, DateTimeOffset.Now, TestServices.Make());
        editor.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();
        editor.Canvas.Focus();
        return editor;
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None, string? symbol = null)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, symbol);
        window.KeyRelease(key, modifiers, PhysicalKey.None, symbol);
    }

    private static void Click(Window window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(centre);
        window.MouseDown(centre.Value, MouseButton.Left);
        window.MouseUp(centre.Value, MouseButton.Left);
    }

    [AvaloniaFact]
    public void TheToolbarListsEveryToolInItsGroups()
    {
        var editor = Open();
        Assert.Equal(
        [
            Tool.Select, Tool.Crop,
            Tool.Arrow, Tool.Line, Tool.Rectangle, Tool.Oval, Tool.Freehand, Tool.Highlighter,
            Tool.Text, Tool.Step, Tool.Image,
            Tool.Spotlight, Tool.Magnifier, Tool.Measure,
            Tool.Blur, Tool.Pixelate, Tool.Erase,
        ], editor.ToolButtons.Select(b => b.Tool));
        Assert.Equal(4, editor.GroupDividers);
        Assert.Equal("Arrow (A)", ToolTip.GetTip(editor.ToolButtons[2].Button));
        Assert.Equal("Select (V), or hold Ctrl with any tool", ToolTip.GetTip(editor.ToolButtons[0].Button));
    }

    [AvaloniaFact]
    public void ClickingAToolButtonPicksItAndTheButtonFollowsTheSession()
    {
        var editor = Open();
        var rectangle = editor.ToolButtons.Single(b => b.Tool == Tool.Rectangle).Button;
        Click(editor, rectangle);
        Assert.Equal(Tool.Rectangle, editor.Canvas.Session.Tool);
        Assert.True(rectangle.IsChecked);
        Assert.False(editor.ToolButtons.Single(b => b.Tool == Tool.Arrow).Button.IsChecked);

        editor.Canvas.Focus();
        Press(editor, Key.E, symbol: "e");
        Assert.True(editor.ToolButtons.Single(b => b.Tool == Tool.Erase).Button.IsChecked);
        Assert.False(rectangle.IsChecked);
    }

    [AvaloniaFact]
    public void ZoomKeysStepAndFit()
    {
        var editor = Open();
        Assert.Equal(1, editor.Canvas.Zoom);
        Press(editor, Key.OemPlus, RawInputModifiers.Control, "=");
        Assert.Equal(1.25, editor.Canvas.Zoom, 9);
        Press(editor, Key.OemMinus, RawInputModifiers.Control, "-");
        Assert.Equal(1, editor.Canvas.Zoom, 9);
        Press(editor, Key.D0, RawInputModifiers.Control, "0");
        Assert.True(editor.Canvas.Zoom > 1);
        Press(editor, Key.D1, RawInputModifiers.Control, "1");
        Assert.Equal(1, editor.Canvas.Zoom);
    }

    [AvaloniaFact]
    public void TheWindowCannotBeNarrowerThanItsToolbar()
    {
        var editor = Open();
        Assert.True(editor.MinWidth >= editor.Toolbar.DesiredSize.Width);
        Assert.True(editor.Toolbar.DesiredSize.Width > 17 * 28);
    }

    [AvaloniaFact]
    public void OnAScreenNarrowerThanTheToolbarTheWheelScrollsItsToolsIntoView()
    {
        var editor = Open();
        // As the window is on a screen narrower than its toolbar.
        editor.MinWidth = 0;
        editor.Width = 600;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();
        double Left(Control control) => control.TranslatePoint(default, editor)!.Value.X;
        double Right(Control control) => Left(control) + control.Bounds.Width;
        var last = editor.Toolbar.GetVisualDescendants().OfType<Button>()
            .Where(button => button != editor.LibraryButton).MaxBy(Left)!;
        Assert.True(Right(editor.LibraryButton) <= 600, $"library ends at {Right(editor.LibraryButton)}, window {editor.ClientSize.Width}");
        Assert.True(Right(last) > Left(editor.LibraryButton));

        for (var notch = 0; notch < 20; notch++) editor.MouseWheel(new Point(300, 20), new Avalonia.Vector(0, -1));
        editor.UpdateLayout();
        Assert.True(Right(last) <= Left(editor.LibraryButton));
    }

    [Fact]
    public void TheTitleNamesTheCaptureTime()
    {
        var english = CultureInfo.GetCultureInfo("en-US");
        var captured = new DateTimeOffset(2026, 9, 25, 9, 41, 12, TimeSpan.Zero);
        // Current culture data puts a narrow no-break space before AM, so the parts are checked.
        var today = EditorWindow.TitleFor(captured, captured.AddHours(3), TimeZoneInfo.Utc, english);
        Assert.StartsWith("Capture at 9:41:12", today);
        Assert.EndsWith("AM", today);
        var later = EditorWindow.TitleFor(captured, captured.AddDays(1), TimeZoneInfo.Utc, english);
        Assert.StartsWith("Capture at 9/25/2026 9:41:12", later);
    }

    [AvaloniaFact]
    public void EveryIconButtonHasAnAccessibleName()
    {
        // The buttons show only icons, so a screen reader needs a name for each.
        var editor = Open(Tool.Rectangle);
        foreach (var (tool, button) in editor.ToolButtons)
            Assert.Equal(tool.Title(), Avalonia.Automation.AutomationProperties.GetName(button));
        var bar = editor.StyleBar;
        Assert.Equal("Colour #FF3B30", Avalonia.Automation.AutomationProperties.GetName(bar.ColorButton!));
        foreach (var chip in bar.SizeChips.Concat(bar.FillChips).Concat(bar.CornerChips))
            Assert.False(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(chip)));
    }
}
