using Avalonia.Input;
using SkiaSharp;

namespace Tinysnap.Linux;

/// <summary>Avalonia's own X11 clipboard, which XWayland hands on to Wayland apps. Images go as
/// PNG, which keeps their DPI and their see-through pixels.</summary>
// ponytail: ChangeCount counts only Tinysnap's own copies; XFixes selection events if another
// app's copy should stop a thumbnail copying itself.
internal sealed class LinuxClipboard(Func<Avalonia.Input.Platform.IClipboard?> clipboard) : Tinysnap.Platform.IClipboard
{
    private static readonly DataFormat<byte[]> Png = DataFormat.CreateBytesPlatformFormat("image/png");

    public uint ChangeCount { get; private set; }

    public bool SetImage(SKImage image, byte[] png, double dpi)
    {
        var item = new DataTransferItem();
        item.Set(Png, png);
        return Set(item);
    }

    public bool SetText(string text) => Set(DataTransferItem.CreateText(text));

    private bool Set(DataTransferItem item)
    {
        if (clipboard() is not { } board) return false;
        var data = new DataTransfer();
        data.Add(item);
        // X11 hands the data over when another app asks, so there is nothing to wait for here.
        _ = board.SetDataAsync(data);
        ChangeCount++;
        return true;
    }
}
