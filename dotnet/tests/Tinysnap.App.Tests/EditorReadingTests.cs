using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SkiaSharp;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using static Tinysnap.App.Tests.TestServices;
using Point = Avalonia.Point;

namespace Tinysnap.App.Tests;

public class EditorReadingTests
{
    /// <summary>An editor whose reads are recorded: each image's size, whether it was for codes,
    /// and its pixel at (170, 130), where the test arrow runs.</summary>
    private static (EditorWindow Editor, List<(int Width, int Height, bool Codes, SKColor Pixel)> Reads) Open(Tool tool = Tool.Arrow)
    {
        var reads = new List<(int, int, bool, SKColor)>();
        var services = Make() with
        {
            Read = (image, codes, _) =>
            {
                using var bitmap = SKBitmap.FromImage(image);
                var pixel = bitmap.Width > 170 && bitmap.Height > 130 ? bitmap.GetPixel(170, 130) : SKColors.Empty;
                reads.Add((image.Width, image.Height, codes, pixel));
                return Task.CompletedTask;
            },
        };
        return (Editor(services, tool), reads);
    }

    private static void CopyText(EditorWindow editor) => Press(editor, Key.C, RawInputModifiers.Control | RawInputModifiers.Shift, "C");

    [AvaloniaFact]
    public async Task CopyTextCopiesTheDraggedBox()
    {
        var (editor, reads) = Open();
        CopyText(editor);
        Assert.True(editor.Canvas.IsPickingText);
        Assert.True(editor.CopyTextButton.IsChecked);
        Assert.True(editor.TextHint.IsVisible);
        Draw(editor);
        await editor.WhenRead();
        // Canvas points (60, 50) to (110, 80) at 2x: a box 100 by 60 pixels.
        var read = Assert.Single(reads);
        Assert.Equal((100, 60, false), (read.Width, read.Height, read.Codes));
        // It stays on for the next drag, and nothing was drawn.
        Assert.True(editor.Canvas.IsPickingText);
        Assert.Empty(editor.Canvas.Session.Display.Annotations);
    }

    [AvaloniaFact]
    public async Task AClickCopiesAllTheText()
    {
        var (editor, reads) = Open();
        CopyText(editor);
        var point = editor.Canvas.TranslatePoint(new Point(60, 50), editor)!.Value;
        editor.MouseDown(point, MouseButton.Left);
        editor.MouseUp(point, MouseButton.Left);
        await editor.WhenRead();
        Assert.Equal((400, 300), (reads[0].Width, reads[0].Height));
    }

    [AvaloniaFact]
    public async Task CopyTextReadsWhatTheCaptureShows()
    {
        // What is read is what an export holds, so an erase or a blur hides what it covers.
        var (editor, reads) = Open();
        Draw(editor);
        CopyText(editor);
        var point = editor.Canvas.TranslatePoint(new Point(20, 20), editor)!.Value;
        editor.MouseDown(point, MouseButton.Left);
        editor.MouseUp(point, MouseButton.Left);
        await editor.WhenRead();
        Assert.True(reads[0].Pixel.Red > 200 && reads[0].Pixel.Green < 100, $"the arrow is in what was read: {reads[0].Pixel}");
    }

    [AvaloniaFact]
    public void EscEndsCopyTextAndTheToolComesBack()
    {
        var (editor, _) = Open(Tool.Rectangle);
        CopyText(editor);
        Press(editor, Key.Escape);
        Assert.False(editor.Canvas.IsPickingText);
        Assert.False(editor.CopyTextButton.IsChecked);
        Assert.False(editor.TextHint.IsVisible);
        Assert.Equal(Tool.Rectangle, editor.Canvas.Session.Tool);
        Assert.True(editor.IsVisible);
    }

    [AvaloniaFact]
    public void PickingAToolOrPressingCopyTextAgainEndsIt()
    {
        var (editor, _) = Open();
        CopyText(editor);
        Press(editor, Key.O, symbol: "o");
        Assert.False(editor.Canvas.IsPickingText);
        Assert.Equal(Tool.Oval, editor.Canvas.Session.Tool);
        CopyText(editor);
        CopyText(editor);
        Assert.False(editor.Canvas.IsPickingText);
    }

    [AvaloniaFact]
    public async Task CtrlShiftRScansTheCapture()
    {
        var (editor, reads) = Open();
        Press(editor, Key.R, RawInputModifiers.Control | RawInputModifiers.Shift, "R");
        await editor.WhenRead();
        Assert.Equal((400, 300, true), (reads[0].Width, reads[0].Height, reads[0].Codes));
        Assert.False(editor.Canvas.IsPickingText);
    }
}
