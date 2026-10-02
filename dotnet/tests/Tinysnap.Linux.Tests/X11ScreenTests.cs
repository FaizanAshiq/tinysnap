using System.Diagnostics;
using SkiaSharp;
using Rect = Tinysnap.Core.Rect;

namespace Tinysnap.Linux.Tests;

/// <summary>Run under <c>xvfb-run</c> at 1280 by 800 with openbox, as CI does.</summary>
public class X11ScreenTests
{
    [Fact]
    public void TheMonitorIsThere()
    {
        Linux.Only();
        using var screen = X11Screen.TryOpen()!;
        var monitor = Assert.Single(screen.Monitors());
        Assert.Equal((1280.0, 800.0), (monitor.Bounds.Width, monitor.Bounds.Height));
        Assert.NotNull(screen.Pointer());
    }

    [Fact]
    public void AGrabReadsTheScreensPixelsOpaque()
    {
        Linux.Only();
        // xsetroot paints the root window a known colour.
        Process.Start("xsetroot", ["-solid", "#3366cc"])!.WaitForExit();
        using var screen = X11Screen.TryOpen()!;
        using var image = screen.Grab(new Rect(0, 0, 100, 50))!;
        using var bitmap = SKBitmap.FromImage(image);
        Assert.Equal((100, 50), (image.Width, image.Height));
        Assert.Equal(new SKColor(0x33, 0x66, 0xcc, 0xff), bitmap.GetPixel(10, 10));
    }

    [Fact]
    public void AGrabPastTheScreensEdgeIsCutToIt()
    {
        Linux.Only();
        using var screen = X11Screen.TryOpen()!;
        using var image = screen.Grab(new Rect(1200, 750, 200, 200))!;
        Assert.Equal((80, 50), (image.Width, image.Height));
        Assert.Null(screen.Grab(new Rect(2000, 2000, 10, 10)));
    }

    private static string Run(string program, params string[] args)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output;
    }

    [Fact]
    public void AWindowAskedForAsAPagerWouldGetsTheKeyboard()
    {
        // A hotkey reaches Tinysnap with no input of its own, and GNOME refused it focus.
        Linux.Only();
        using var first = Process.Start("xmessage", ["-geometry", "+100+100", "-name", "first", "-title", "first", "first"])!;
        Thread.Sleep(700);
        using var second = Process.Start("xmessage", ["-geometry", "+300+300", "-name", "second", "-title", "second", "second"])!;
        try
        {
            string firstWindow = "";
            for (var i = 0; i < 30 && (firstWindow.Length == 0 || Run("xdotool", "getactivewindow") == firstWindow); i++, Thread.Sleep(100))
                firstWindow = Run("xdotool", "search", "--name", "^first$").Split('\n')[0];
            Assert.NotEqual(firstWindow, Run("xdotool", "getactivewindow"));
            using var screen = X11Screen.TryOpen()!;
            screen.Activate(nuint.Parse(firstWindow));
            var active = "";
            for (var i = 0; i < 30 && active != firstWindow; i++, Thread.Sleep(100)) active = Run("xdotool", "getactivewindow");
            Assert.Equal(firstWindow, active);
        }
        finally
        {
            first.Kill();
            second.Kill();
        }
    }

    [Fact]
    public void TheWindowManagersWindowsAreListedFrontToBack()
    {
        Linux.Only();
        using var first = Process.Start("xmessage", ["-geometry", "+100+100", "-name", "first", "-title", "first", "first"])!;
        Thread.Sleep(700);
        using var second = Process.Start("xmessage", ["-geometry", "+300+300", "-name", "second", "-title", "second", "second"])!;
        try
        {
            using var screen = X11Screen.TryOpen()!;
            var titles = new List<string>();
            for (var i = 0; i < 30 && titles.Count < 2; i++, Thread.Sleep(100))
                titles = [.. screen.Windows().Select(w => w.Title).Where(t => t is "first" or "second")];
            Assert.Equal(["second", "first"], titles);
            var shown = screen.Windows().First(w => w.Title == "second").Bounds;
            Assert.InRange(shown.X, 290, 310);
        }
        finally
        {
            first.Kill();
            second.Kill();
        }
    }
}
