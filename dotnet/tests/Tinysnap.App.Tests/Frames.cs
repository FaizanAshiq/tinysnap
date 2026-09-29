using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using SkiaSharp;

namespace Tinysnap.App.Tests;

/// <summary>Reads back what a headless window drew.</summary>
internal static class Frames
{
    public static SKBitmap Capture(TopLevel window)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var locked = frame.Lock();
        var type = locked.Format == PixelFormats.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
        var info = new SKImageInfo(locked.Size.Width, locked.Size.Height, type, SKAlphaType.Premul);
        using var view = new SKBitmap();
        view.InstallPixels(info, locked.Address, locked.RowBytes);
        // A copy, since the frame's pixels are only ours while it is locked.
        return view.Copy();
    }
}
