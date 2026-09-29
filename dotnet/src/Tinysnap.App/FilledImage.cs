using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;

namespace Tinysnap.App;

/// <summary>An image stretched to fill the control, smoothly at any size, for pins and the
/// thumbnail. It holds its own share of <paramref name="image"/> for each frame it draws.</summary>
internal sealed class FilledImage(SharedImage image) : Control
{
    private static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var held = image.Acquire();
        var target = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);
        context.Custom(new SkiaDraw(bounds, canvas => canvas.DrawImage(held.Image, target, Smooth), held.Release));
    }
}
