using System.Text.Json.Nodes;
using SkiaSharp;

namespace Tinysnap.Core;

public enum BackdropFill
{
    /// <summary>Two colours taken from the capture itself.</summary>
    Gradient,
    Solid,
    /// <summary>The desktop picture, softened.</summary>
    Wallpaper,
    /// <summary>Nothing: the PNG is see-through around the capture and its shadow.</summary>
    Clear,
}

public enum BackdropPadding { Small, Medium, Large }

public enum BackdropShadow { None, Soft, Strong }

public static class BackdropOptions
{
    public static double Points(this BackdropPadding padding) => padding switch
    {
        BackdropPadding.Small => 24,
        BackdropPadding.Medium => 48,
        _ => 88,
    };
}

/// <summary>The desktop picture a wallpaper fill shows, softened once, when it was read.
/// The id names its file in a library entry, so it is written once rather than on every
/// edit, and two wallpapers are the same when their ids are.</summary>
public sealed record BackdropWallpaper(Guid Id, PastedImage Image)
{
    public bool Equals(BackdropWallpaper? other) => other is not null && Id == other.Id;
    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>The frame drawn around the output for sharing: a fill, padding, rounded corners
/// on the capture and a shadow under it. A document has none until one is turned on.</summary>
public sealed record Backdrop(BackdropFill Fill, string ColorHex, BackdropPadding Padding, CornerSize Corners,
                              BackdropShadow Shadow, BackdropWallpaper? Wallpaper = null)
{
    /// <summary>The corners the panel offers: square, round and rounder.</summary>
    public static readonly IReadOnlyList<CornerSize> CornerChoices = [CornerSize.Square, CornerSize.Medium, CornerSize.Large];

    public static readonly Backdrop Defaults =
        new(BackdropFill.Gradient, "#007AFF", BackdropPadding.Medium, CornerSize.Medium, BackdropShadow.Soft);

    /// <summary>A wallpaper softened once, when it is read, so it sits behind the capture
    /// without competing with it. The blur scales with the picture, and its edges are held so
    /// they do not fade to nothing.</summary>
    public static SKImage? Soften(SKImage image)
    {
        var sigma = (float)(Math.Max(image.Width, image.Height) * 0.012);
        using var surface = SKSurface.Create(Renderer.Info(image.Width, image.Height));
        if (surface is null) return null;
        surface.Canvas.Clear(SKColors.Transparent);
        using var blur = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { ImageFilter = blur };
        surface.Canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint);
        return surface.Snapshot();
    }

    /// <summary>The settings only. A wallpaper's pixels are never part of them: preferences
    /// keep no image, and a library entry keeps it as its own file.</summary>
    public JsonObject ToJson() => new()
    {
        ["fill"] = Json.Wire(Fill),
        ["colorHex"] = ColorHex,
        ["padding"] = Json.Wire(Padding),
        ["corners"] = Json.Wire(Corners),
        ["shadow"] = Json.Wire(Shadow),
    };

    /// <summary>Each value falls back on its own, as Style and Preferences do.</summary>
    public static Backdrop FromJson(JsonNode? node)
    {
        var o = node as JsonObject ?? [];
        var fallback = Defaults;
        var hex = Json.String(o, "colorHex");
        var corners = Json.Enum<CornerSize>(o, "corners");
        return new Backdrop(
            Json.Enum<BackdropFill>(o, "fill") ?? fallback.Fill,
            hex is not null && Palette.Components(hex) is not null ? hex : fallback.ColorHex,
            Json.Enum<BackdropPadding>(o, "padding") ?? fallback.Padding,
            corners is { } c && CornerChoices.Contains(c) ? c : fallback.Corners,
            Json.Enum<BackdropShadow>(o, "shadow") ?? fallback.Shadow);
    }
}
