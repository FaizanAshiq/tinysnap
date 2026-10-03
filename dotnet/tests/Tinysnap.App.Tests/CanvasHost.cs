using Avalonia.Controls;
using Avalonia.Layout;
using SkiaSharp;
using Tinysnap.App.Editing;
using Tinysnap.Core;

namespace Tinysnap.App.Tests;

/// <summary>A canvas in a headless window, its top left corner at the window's, so window and
/// canvas coordinates are the same.</summary>
internal static class CanvasHost
{
    public static (Window Window, CanvasControl Canvas) Open(Capture capture, double zoom = 1, Tool tool = Tool.Arrow)
    {
        var canvas = new CanvasControl(new EditorSession(new Document(capture), tool))
        {
            Zoom = zoom,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Window { Width = 900, Height = 700, Content = canvas };
        window.Show();
        return (window, canvas);
    }

    public static Capture Blank(int width, int height, double scale = 2) => Capture(width, height, scale, _ => { });

    /// <summary>Two black cards 32 pixels apart on white, as MeasureReadingTests draws them.</summary>
    public static Capture TwoCards() => Capture(400, 200, 2, canvas =>
    {
        using var black = new SKPaint { Color = SKColors.Black };
        canvas.DrawRect(new SKRect(40, 60, 140, 140), black);
        canvas.DrawRect(new SKRect(172, 60, 272, 140), black);
    });

    public static Capture Capture(int width, int height, double scale, Action<SKCanvas> paint)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        paint(surface.Canvas);
        return new Capture(surface.Snapshot(), scale);
    }
}
