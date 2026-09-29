using SkiaSharp;
using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.App.Tests;

/// <summary>Frozen monitors for tests, each a solid colour so a capture shows where it came from.</summary>
internal static class Screens
{
    public static FrozenScreen Frozen(Rect bounds, double scale, SKColor? fill = null)
    {
        var info = new SKImageInfo((int)bounds.Width, (int)bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul,
                                   SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(fill ?? SKColors.White);
        return new FrozenScreen(bounds, scale, surface.Snapshot());
    }

    public static FrozenDesktop Desktop(params FrozenScreen[] screens) => new(screens, []);
}
