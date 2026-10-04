using System.Globalization;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>Five steps, thinnest first. <c>small</c>, <c>medium</c> and <c>large</c> keep their
/// old names, so a preferences file from before there were five still loads.</summary>
public enum StyleSize { ExtraSmall, Small, Medium, Large, ExtraLarge }

/// <summary>How round a box's corners are, from square to fully round.</summary>
public enum CornerSize { Square, Small, Medium, Large, Full }

public static class StyleSizes
{
    /// <summary>One step down, for the [ key. Stops at the thinnest.</summary>
    public static StyleSize Thinner(this StyleSize size) => (StyleSize)Math.Max(0, (int)size - 1);

    /// <summary>One step up, for the ] key. Stops at the thickest.</summary>
    public static StyleSize Thicker(this StyleSize size) => (StyleSize)Math.Min((int)StyleSize.ExtraLarge, (int)size + 1);

    /// <summary>The radius in points, or null for fully round: half the box's shorter side,
    /// which makes a square spotlight a circle.</summary>
    public static double? Points(this CornerSize corners) => corners switch
    {
        CornerSize.Square => 0,
        CornerSize.Small => 4,
        CornerSize.Medium => 10,
        CornerSize.Large => 20,
        _ => null,
    };
}

/// <summary>Where each line of a text sits in the box its widest line makes.</summary>
public enum TextAlign { Left, Center, Right }

public sealed record Style
{
    public const double OpacityMin = 0.1;
    public const double OpacityMax = 1;

    private readonly double opacity = 1;

    public string ColorHex { get; init; }
    public StyleSize Size { get; init; }

    /// <summary>Rectangles and ovals filled instead of outlined, and text set on a box in its colour.</summary>
    public bool Filled { get; init; }

    /// <summary>Rectangles, spotlights, blurs, pixelates and pasted images.</summary>
    public CornerSize Corners { get; init; }

    /// <summary>Pasted images only: 0.1 to 1, for lining one up against the capture. Held
    /// to that range however it is set, as the Swift initialiser holds it.</summary>
    public double Opacity
    {
        get => opacity;
        init => opacity = Math.Min(Math.Max(value, OpacityMin), OpacityMax);
    }

    /// <summary>Pasted images only: drawn with the difference blend, so where the image
    /// matches what is under it the result is black and changes stand out.</summary>
    public bool Difference { get; init; }

    /// <summary>Text only: each line set left, centred or right.</summary>
    public TextAlign Align { get; init; }

    public Style(string colorHex, StyleSize size = StyleSize.Medium, bool filled = false,
                 CornerSize corners = CornerSize.Medium, double opacity = 1, bool difference = false,
                 TextAlign align = TextAlign.Left)
    {
        ColorHex = colorHex;
        Size = size;
        Filled = filled;
        Corners = corners;
        Opacity = opacity;
        Difference = difference;
        Align = align;
    }

    public JsonObject ToJson() => new()
    {
        ["colorHex"] = ColorHex,
        ["size"] = Json.Wire(Size),
        ["filled"] = Filled,
        ["corners"] = Json.Wire(Corners),
        ["opacity"] = Opacity,
        ["difference"] = Difference,
        ["align"] = Json.Wire(Align),
    };

    /// <summary>Every key is optional and a bad value falls back on its own, so one hand
    /// edited mistake in preferences.json costs that one value rather than the whole file.</summary>
    public static Style FromJson(JsonNode? node)
    {
        var o = node as JsonObject ?? [];
        var hex = Json.String(o, "colorHex");
        var colorHex = hex is not null && Palette.Components(hex) is not null ? hex : Palette.Red;
        var size = Json.Enum<StyleSize>(o, "size") ?? StyleSize.Medium;
        var filled = Json.Bool(o, "filled") ?? false;
        var wasSquare = Json.Bool(o, "sharpCorners") ?? false;
        var corners = Json.Enum<CornerSize>(o, "corners") ?? (wasSquare ? CornerSize.Square : CornerSize.Medium);
        // Out of range means a hand edit gone wrong, which is read as fully solid.
        var read = Json.Number(o, "opacity");
        var opacity = read is { } value && value <= 1 ? Math.Max(value, OpacityMin) : 1;
        var difference = Json.Bool(o, "difference") ?? false;
        var align = Json.Enum<TextAlign>(o, "align") ?? TextAlign.Left;
        return new Style(colorHex, size, filled, corners, opacity, difference, align);
    }
}

public static class Palette
{
    public const string Red = "#FF3B30";
    public const string Yellow = "#FFCC00";

    /// <summary>The eight swatches in the style popover, in the order shown.</summary>
    public static readonly IReadOnlyList<string> Swatches =
        ["#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#007AFF", "#AF52DE", "#000000", "#FFFFFF"];

    /// <summary>The sRGB components of "#RRGGBB", or null for anything else.</summary>
    public static (double Red, double Green, double Blue)? Components(string hex)
    {
        var digits = hex.StartsWith('#') ? hex[1..] : hex;
        if (digits.Length != 6 || !digits.All(Uri.IsHexDigit)) return null;
        var value = uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (((value >> 16) & 0xFF) / 255.0, ((value >> 8) & 0xFF) / 255.0, (value & 0xFF) / 255.0);
    }

    public static SKColor Color(string hex, double alpha = 1)
    {
        var rgb = Components(hex) ?? (1, 59 / 255.0, 48 / 255.0);
        return new SKColor(Byte(rgb.Red), Byte(rgb.Green), Byte(rgb.Blue), Byte(alpha));
    }

    public static string Hex(double red, double green, double blue) =>
        $"#{Byte(red):X2}{Byte(green):X2}{Byte(blue):X2}";

    private static byte Byte(double value) => (byte)Geometry.Round(Math.Max(0, Math.Min(1, value)) * 255);
}
