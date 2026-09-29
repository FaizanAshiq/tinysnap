using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Tinysnap.Core;

namespace Tinysnap.Windows.Tests;

/// <summary>One class, so the tests take the one system clipboard in turn.</summary>
public class ClipboardTests
{
    private static SKImage HalfClear()
    {
        // 40 by 20: the left half see-through, the right half blue.
        using var surface = SKSurface.Create(new SKImageInfo(40, 20, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        using var blue = new SKPaint { Color = SKColors.Blue };
        surface.Canvas.DrawRect(20, 0, 20, 20, blue);
        return surface.Snapshot();
    }

    [Fact]
    public void APngAndADibArePutOnTheClipboard()
    {
        using var clipboard = new Win32Clipboard();
        var image = HalfClear();
        var before = clipboard.ChangeCount;
        Assert.True(clipboard.SetImage(image, Png.Encode(image, 144)!, 144));
        Assert.NotEqual(before, clipboard.ChangeCount);
        var png = Read.Format(Read.PngFormat);
        Assert.NotNull(png);
        var decoded = Png.Decode(png);
        Assert.NotNull(decoded);
        Assert.Equal(40, decoded.Value.Image.Width);
        Assert.Equal(2, decoded.Value.Scale);
        Assert.NotNull(Read.Format(Read.CF_DIBV5));
    }

    [Fact]
    public void TheDibKeepsSeeThroughPixels()
    {
        using var clipboard = new Win32Clipboard();
        var image = HalfClear();
        Assert.True(clipboard.SetImage(image, Png.Encode(image, 72)!, 72));
        var dib = Read.Format(Read.CF_DIBV5);
        Assert.NotNull(dib);
        Assert.Equal(124, BitConverter.ToInt32(dib, 0));
        Assert.Equal(40, BitConverter.ToInt32(dib, 4));
        Assert.Equal(20, BitConverter.ToInt32(dib, 8));
        // Blue, green, red, alpha, bottom row first; the top row is the last.
        var topRow = 124 + 19 * 40 * 4;
        Assert.Equal(0, dib[topRow + 3]);
        var rightPixel = topRow + 30 * 4;
        Assert.Equal([255, 0, 0, 255], dib[rightPixel..(rightPixel + 4)]);
    }

    [Fact]
    public void TextGoesOnTheClipboard()
    {
        using var clipboard = new Win32Clipboard();
        Assert.True(clipboard.SetText("#FF3B30"));
        var text = Read.Format(Read.CF_UNICODETEXT);
        Assert.NotNull(text);
        Assert.Equal("#FF3B30", Encoding.Unicode.GetString(text).TrimEnd('\0'));
    }

    [Fact]
    public void ACopyRetriesWhileTheClipboardIsBusy()
    {
        using var clipboard = new Win32Clipboard();
        using var held = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            Assert.True(Read.OpenClipboard(0));
            held.Set();
            Thread.Sleep(60);
            Read.CloseClipboard();
        });
        holder.Start();
        held.Wait(TestContext.Current.CancellationToken);
        Assert.True(clipboard.SetText("after the wait"));
        holder.Join();
    }
}

/// <summary>Reads the clipboard back, for checking what was put there.</summary>
internal static class Read
{
    public const uint CF_UNICODETEXT = 13;
    public const uint CF_DIBV5 = 17;
    public static readonly uint PngFormat = RegisterClipboardFormatW("PNG");

    public static byte[]? Format(uint format)
    {
        if (!OpenClipboard(0)) return null;
        try
        {
            var memory = GetClipboardData(format);
            if (memory == 0) return null;
            var size = (int)GlobalSize(memory);
            var pointer = GlobalLock(memory);
            try
            {
                var bytes = new byte[size];
                Marshal.Copy(pointer, bytes, 0, size);
                return bytes;
            }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);
}
