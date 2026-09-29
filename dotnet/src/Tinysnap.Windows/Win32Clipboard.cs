using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Tinysnap.Platform;
using static Tinysnap.Windows.Native;

namespace Tinysnap.Windows;

/// <summary>The clipboard through Win32: a PNG under the registered "PNG" format, which Office,
/// browsers and chat apps read with its transparency, and a <c>CF_DIBV5</c> bitmap for the rest,
/// from which Windows makes the older formats itself.</summary>
internal sealed class Win32Clipboard : IClipboard, IDisposable
{
    private static readonly uint PngFormat = RegisterClipboardFormatW("PNG");

    /// <summary>The clipboard must have an owner window, or what is put there is refused.</summary>
    private readonly nint owner = CreateWindowExW(0, "Static", "", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);

    public uint ChangeCount => GetClipboardSequenceNumber();

    public bool SetImage(SKImage image, byte[] png, double dpi) =>
        Dib(image, dpi) is { } dib && Put((PngFormat, png), (CF_DIBV5, dib));

    public bool SetText(string text) => Put((CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0")));

    private bool Put(params (uint Format, byte[] Data)[] items)
    {
        if (!Open()) return false;
        try
        {
            if (!EmptyClipboard()) return false;
            foreach (var (format, data) in items)
            {
                var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
                if (memory == 0) return false;
                var pointer = GlobalLock(memory);
                Marshal.Copy(data, 0, pointer, data.Length);
                GlobalUnlock(memory);
                // Taken over by the clipboard once set; freed here only when refused.
                if (SetClipboardData(format, memory) == 0)
                {
                    GlobalFree(memory);
                    return false;
                }
            }
            return true;
        }
        finally { CloseClipboard(); }
    }

    /// <summary>Another app can hold the clipboard open for a moment, so a copy tries for about a
    /// fifth of a second before it gives up.</summary>
    private bool Open()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(owner)) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    /// <summary>A packed <c>BITMAPV5HEADER</c> and its pixels: bottom-up rows of blue, green,
    /// red and straight alpha, in sRGB, at the image's DPI.</summary>
    internal static byte[]? Dib(SKImage image, double dpi)
    {
        int width = image.Width, height = image.Height, stride = width * 4;
        var pixels = new byte[stride * height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
            if (!image.ReadPixels(info, handle.AddrOfPinnedObject(), stride, 0, 0)) return null;
        }
        finally { handle.Free(); }

        using var dib = new MemoryStream(124 + pixels.Length);
        using var write = new BinaryWriter(dib);
        var perMetre = (int)Math.Round(dpi / 0.0254);
        write.Write(124);                 // bV5Size
        write.Write(width);
        write.Write(height);              // positive: bottom-up
        write.Write((short)1);            // planes
        write.Write((short)32);           // bits a pixel
        write.Write(3);                   // BI_BITFIELDS
        write.Write(pixels.Length);
        write.Write(perMetre);
        write.Write(perMetre);
        write.Write(0);                   // colours used
        write.Write(0);                   // colours important
        write.Write(0x00FF0000u);         // red mask
        write.Write(0x0000FF00u);         // green mask
        write.Write(0x000000FFu);         // blue mask
        write.Write(0xFF000000u);         // alpha mask
        write.Write(0x73524742u);         // LCS_sRGB
        write.Write(new byte[36]);        // endpoints, unused for sRGB
        write.Write(new byte[12]);        // gamma, unused for sRGB
        write.Write(4);                   // LCS_GM_IMAGES
        write.Write(0);                   // profile data
        write.Write(0);                   // profile size
        write.Write(0);                   // reserved
        for (var row = height - 1; row >= 0; row--) write.Write(pixels, row * stride, stride);
        return dib.ToArray();
    }

    public void Dispose() => DestroyWindow(owner);
}
