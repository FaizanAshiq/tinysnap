using System.Runtime.InteropServices;
using SkiaSharp;
using Tinysnap.Core;

namespace Tinysnap.Windows.Tests;

/// <summary>Run on a real Windows desktop, in CI; a Mac builds these but cannot run them.</summary>
public class PlatformTests
{
    [Fact]
    public void EveryMonitorFreezesAtItsPixelSize()
    {
        var desktop = new GdiScreenCapture().Freeze();
        Assert.NotEmpty(desktop.Screens);
        foreach (var screen in desktop.Screens)
        {
            Assert.Equal((int)screen.Bounds.Width, screen.Image.Width);
            Assert.Equal((int)screen.Bounds.Height, screen.Image.Height);
            Assert.True(screen.Scale >= 1);
        }
    }

    [Fact]
    public void AFrozenMonitorIsOpaque()
    {
        // GDI leaves the alpha byte undefined, and a capture read with it came out see-through.
        var screen = new GdiScreenCapture().Freeze().Screens[0];
        using var bitmap = SKBitmap.FromImage(screen.Image);
        foreach (var (x, y) in new[] { (0, 0), (bitmap.Width / 2, bitmap.Height / 2), (bitmap.Width - 1, bitmap.Height - 1) })
            Assert.Equal(255, bitmap.GetPixel(x, y).Alpha);
    }

    [Fact]
    public void TheWindowListIsFrontToBackAndSkipsHiddenOnes()
    {
        using var back = TestWindow.Show("Tinysnap test back", 100, 100);
        using var front = TestWindow.Show("Tinysnap test front", 160, 160);
        var titles = Win32Windows.List().Select(w => w.Title).ToList();
        Assert.Contains("Tinysnap test front", titles);
        Assert.True(titles.IndexOf("Tinysnap test front") < titles.IndexOf("Tinysnap test back"));
        var listed = Win32Windows.List().Single(w => w.Title == "Tinysnap test front");
        Assert.True(listed.Bounds.Width >= 40 && listed.Bounds.Height >= 40);

        front.Hide();
        Assert.DoesNotContain("Tinysnap test front", Win32Windows.List().Select(w => w.Title));
    }

    [Fact]
    public void AHotkeyRegistersAndUnregisters()
    {
        using var hotkeys = new Win32Hotkeys();
        // Ctrl+Alt+Shift+F12: nothing a desktop is likely to hold.
        var binding = new HotKeyBinding(0x7B, [ModifierKey.Control, ModifierKey.Alt, ModifierKey.Shift]);
        Assert.True(hotkeys.Register(HotKeyAction.Area, binding));
        hotkeys.UnregisterAll();
        using var another = new Win32Hotkeys();
        Assert.True(another.Register(HotKeyAction.Area, binding));
    }
}

/// <summary>A plain top-level window of our own, for the window list to find.</summary>
internal sealed class TestWindow : IDisposable
{
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_VISIBLE = 0x10000000;
    private const int SW_HIDE = 0;
    private readonly nint handle;

    private TestWindow(nint handle) => this.handle = handle;

    public static TestWindow Show(string title, int x, int y)
    {
        // "Static" is a system class, so no class needs registering.
        var handle = CreateWindowExW(0, "Static", title, WS_OVERLAPPEDWINDOW | WS_VISIBLE, x, y, 400, 300, 0, 0, 0, 0);
        Assert.NotEqual(0, handle);
        return new TestWindow(handle);
    }

    public void Hide() => ShowWindow(handle, SW_HIDE);

    public void Dispose() => DestroyWindow(handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y,
                                               int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
}
