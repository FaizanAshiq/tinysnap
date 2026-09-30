using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using SkiaSharp;
using Tinysnap.App;
using Tinysnap.Core;
using Windows.Media.Ocr;
using ZXing;
using ZXing.Common;
using Point = Tinysnap.Core.Point;

namespace Tinysnap.Windows;

/// <summary><c>Tinysnap.exe --self-check report.txt</c>: runs, once, each path that trimming unused
/// code out of the shipped build could break, writes what passed to the report, and exits 1 if
/// anything failed. CI runs it on the published build, since the tests run the untrimmed one.</summary>
internal static class SelfCheck
{
    public static int Run(string report)
    {
        var lines = new List<string>();
        var failed = false;
        void Check(string name, Func<string?> check)
        {
            try
            {
                var problem = check();
                lines.Add(problem is null ? $"ok      {name}" : $"FAILED  {name}: {problem}");
                failed |= problem is not null;
            }
            catch (Exception error)
            {
                lines.Add($"FAILED  {name}: {error.GetType().Name}: {error.Message}");
                failed = true;
            }
        }

        var capture = Page("Tinysnap self check");
        var document = new Document(capture, annotations:
        [
            Annotation.New(new AnnotationKind.Arrow(new Point(20, 20), new Point(200, 80)), Tool.Arrow.DefaultStyle()),
            Annotation.New(new AnnotationKind.Text(new Point(40, 120), "Note"), Tool.Text.DefaultStyle()),
            Annotation.New(new AnnotationKind.Measure(new Point(10, 150), new Point(300, 150)), Tool.Measure.DefaultStyle()),
        ], backdrop: Backdrop.Defaults);

        Check("library edits round trip through JSON", () =>
        {
            var (json, images) = DocumentArchive.Encode(document, DateTimeOffset.Now);
            var back = DocumentArchive.Decode(json, name => images.GetValueOrDefault(name));
            return back.Annotations.SequenceEqual(document.Annotations) ? null : "annotations differ";
        });
        Check("preferences round trip through JSON", () =>
        {
            var preferences = Preferences.Defaults with { DelaySeconds = 7, KeepLibrary = false };
            return Preferences.FromJson(preferences.ToJson()) == preferences ? null : "preferences differ";
        });
        Check("a framed capture exports as a PNG", () =>
            Exporter.Export(document, ExportScale.Native) is { } exported && Exporter.PngData(exported) is { Length: > 0 } ? null : "no PNG");
        Check("QR codes are read", () =>
        {
            var reading = new WinRtTextReader().Read(QrCode("tinysnap self check"), codes: true).GetAwaiter().GetResult();
            return reading?.Codes.SequenceEqual(["tinysnap self check"]) == true ? null : $"read {reading?.Text ?? "nothing"}";
        });
        Check("text is read", () =>
        {
            if (OcrEngine.TryCreateFromUserProfileLanguages() is null) return null;
            var reading = new WinRtTextReader().Read(capture.Image, codes: false).GetAwaiter().GetResult();
            return reading?.Text.Contains("Tinysnap self check") == true ? null : $"read {reading?.Text ?? "nothing"}";
        });
        Check("the UI starts, its colour picker included", () =>
        {
            using var hotkeys = new Win32Hotkeys();
            using var clipboard = new Win32Clipboard();
            using var instance = new SingleInstance($"TinysnapSelfCheck{Guid.NewGuid():N}");
            var platform = new WindowsPlatform(new GdiScreenCapture(), hotkeys, clipboard, instance);
            AppBuilder.Configure(() => new TinysnapApp(platform)).UsePlatformDetect().SetupWithoutStarting();
            if (Application.Current!.Styles.OfType<StyleInclude>().Single().Loaded is null) return "colour picker styles missing";
            var picker = new ColorView();
            var window = new Window { Content = picker, Width = 300, Height = 400 };
            window.Show();
            window.UpdateLayout();
            var drawn = picker.GetVisualChildren().Any();
            window.Close();
            return drawn ? null : "colour picker has no template";
        });

        File.WriteAllLines(report, lines);
        return failed ? 1 : 0;
    }

    private static Capture Page(string text)
    {
        using var surface = SKSurface.Create(new SKImageInfo(900, 240));
        surface.Canvas.Clear(SKColors.White);
        using var font = TextLayout.Font(48);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        surface.Canvas.DrawText(text, 40, 80 - font.Metrics.Ascent, SKTextAlign.Left, font, ink);
        return new Capture(surface.Snapshot(), 2);
    }

    private static SKImage QrCode(string message)
    {
        var data = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = 240, Height = 240, Margin = 2 },
        }.Write(message);
        return SKImage.FromPixelCopy(new SKImageInfo(data.Width, data.Height, SKColorType.Bgra8888, SKAlphaType.Premul), data.Pixels);
    }
}
