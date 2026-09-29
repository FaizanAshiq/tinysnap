using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SkiaSharp;
using Tinysnap.App;
using Tinysnap.Platform;
using Point = Tinysnap.Core.Point;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.Dev;

/// <summary>The Windows app running on a Mac: a painted desktop stands in for the screen, and a
/// small launcher's buttons stand in for the hotkeys.</summary>
public static class Program
{
    private static Window? launcher;

    [STAThread]
    public static void Main(string[] args)
    {
        var platform = new FakePlatform(new FakeScreenCapture(Freeze, Pointer, windowCornerRadius: 8));
        AppBuilder.Configure(() => new TinysnapApp(platform, ShowLauncher))
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    private static void ShowLauncher(TinysnapApp app)
    {
        var area = new Button { Content = "Capture Area" };
        var fullscreen = new Button { Content = "Capture Fullscreen" };
        area.Click += (_, _) => app.Captures?.CaptureArea();
        fullscreen.Click += (_, _) => app.Captures?.CaptureFullscreen();
        launcher = new Window
        {
            Title = "Tinysnap development",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 8,
                Children =
                {
                    area,
                    fullscreen,
                    new TextBlock { Text = "Space on the overlay picks one of the two painted windows." },
                },
            },
        };
        launcher.Show();
    }

    private static IReadOnlyList<(Rect Bounds, double Scale)> Monitors() =>
        launcher?.Screens.All.Select(s => (new Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Scaling)).ToList()
        ?? [(new Rect(0, 0, 2880, 1800), 2.0)];

    private static Point Pointer()
    {
        var first = Monitors()[0].Bounds;
        return new Point(first.MidX, first.MidY);
    }

    private static FrozenDesktop Freeze()
    {
        var monitors = Monitors();
        var screens = monitors.Select(m => new FrozenScreen(m.Bounds, m.Scale, DesktopPainter.Paint(m.Bounds, m.Scale))).ToList();
        var (first, scale) = monitors[0];
        PickableWindow Window(double x, double y, double width, double height, string title) =>
            new(new Rect(first.X + x * scale, first.Y + y * scale, width * scale, height * scale), title);
        return new FrozenDesktop(screens, [Window(160, 140, 520, 340, "Notes"), Window(560, 300, 600, 400, "Browser")]);
    }
}

/// <summary>A made-up desktop with no personal content: a gradient, two windows and some text.</summary>
internal static class DesktopPainter
{
    public static SKImage Paint(Rect bounds, double scale)
    {
        var info = new SKImageInfo((int)bounds.Width, (int)bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul,
                                   SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        using (var ground = new SKPaint())
        {
            ground.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(info.Width, info.Height),
                                                          [new SKColor(38, 70, 140), new SKColor(120, 60, 150)], SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, info.Width, info.Height, ground);
        }
        canvas.Scale((float)scale);
        Card(canvas, 160, 140, 520, 340, "Notes", "Meeting at 10:30, bring the sketches.");
        Card(canvas, 560, 300, 600, 400, "Browser", "tinysnap.example: capture, mark up, share.");
        return surface.Snapshot();
    }

    private static void Card(SKCanvas canvas, float x, float y, float width, float height, string title, string body)
    {
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 70), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12) };
        canvas.DrawRoundRect(x, y + 8, width, height, 8, 8, shadow);
        using var card = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawRoundRect(x, y, width, height, 8, 8, card);
        using var bar = new SKPaint { Color = new SKColor(236, 236, 240), IsAntialias = true };
        canvas.DrawRect(x, y + 8, width, 28, bar);
        using var text = new SKPaint { Color = new SKColor(30, 30, 35), IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, 14);
        canvas.DrawText(title, x + 14, y + 27, font, text);
        using var bodyFont = new SKFont(SKTypeface.Default, 18);
        canvas.DrawText(body, x + 24, y + 80, bodyFont, text);
    }
}
