using System.Collections.Concurrent;
using System.Globalization;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Tinysnap.Core;

/// <summary>Measures and draws text with Skia and HarfBuzz, so Urdu, Arabic and emoji
/// shape as they would in any app.
///
/// Fonts are made at their size in points and the drawing is scaled up to capture pixels,
/// never made at the pixel size. A system font can change its letter shapes and spacing with
/// size, so a label made at the pixel size came out narrower than the same text typed in
/// the editor.</summary>
public static class TextLayout
{
    // The first family the machine has: Segoe UI on Windows, the system face on a Mac
    // running the tests.
    private static readonly string[] Families =
        ["Segoe UI Variable Text", "Segoe UI", "SF Pro Text", ".AppleSystemUIFont", "Helvetica Neue", "Arial"];

    private static readonly Lazy<SKTypeface> Regular = new(() => Face(SKFontStyle.Normal));
    private static readonly Lazy<SKTypeface> Bold = new(() => Face(SKFontStyle.Bold));

    private static SKTypeface Face(SKFontStyle style)
    {
        foreach (var family in Families)
        {
            var face = SKFontManager.Default.MatchFamily(family, style);
            if (face is not null) return face;
        }
        return SKTypeface.Default;
    }

    /// <summary>Public so the editor's text field types in exactly the font the renderer draws.</summary>
    public static SKFont Font(double points, bool bold = false) =>
        new((bold ? Bold : Regular).Value, (float)points) { Subpixel = true, Edging = SKFontEdging.Antialias };

    internal static double LineHeight(SKFont font) =>
        -font.Metrics.Ascent + font.Metrics.Descent + font.Metrics.Leading;

    /// <summary>Runs of one face each: the UI font where it has the glyph, and for the rest
    /// whatever face the system matches to the character, which is how emoji and scripts the
    /// UI font lacks still draw instead of showing boxes. Joiners, variation selectors and
    /// combining marks stay with the run before them.</summary>
    internal static List<(string Text, SKFont Font)> Runs(string line, SKFont primary)
    {
        var runs = new List<(string, SKFont)>();
        var start = 0;
        SKFont? current = null;
        var index = 0;
        while (index < line.Length)
        {
            var codepoint = char.ConvertToUtf32(line, index);
            var width = char.IsSurrogatePair(line, index) ? 2 : 1;
            SKFont font;
            if (current is not null && Joins(codepoint)) font = current;
            else if (primary.ContainsGlyph(codepoint)) font = primary;
            // Staying in the run's own fallback face spares asking the system for every letter.
            else if (current is not null && !ReferenceEquals(current, primary) && current.ContainsGlyph(codepoint)) font = current;
            else font = Fallback(codepoint, primary) ?? primary;
            if (current is not null && !ReferenceEquals(font, current))
            {
                runs.Add((line[start..index], current));
                start = index;
            }
            current = font;
            index += width;
        }
        if (current is not null) runs.Add((line[start..], current));
        return runs;
    }

    private static bool Joins(int codepoint) =>
        codepoint is 0x200D or (>= 0xFE00 and <= 0xFE0F) or (>= 0x1F3FB and <= 0x1F3FF)
        || CharUnicodeInfo.GetUnicodeCategory(codepoint) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark;

    /// <summary>One font per face and size, kept for the life of the app. Sizes come from the
    /// tools' fixed size tables, so there are only ever a few.</summary>
    private static readonly ConcurrentDictionary<(string Family, int Weight, int Width, SKFontStyleSlant Slant, float Size), SKFont>
        FallbackFonts = new();

    private static SKFont? Fallback(int codepoint, SKFont primary)
    {
        if (SKFontManager.Default.MatchCharacter(null, primary.Typeface.FontStyle, null, codepoint) is not { } face) return null;
        var key = (face.FamilyName, face.FontWeight, face.FontWidth, face.FontSlant, primary.Size);
        return FallbackFonts.GetOrAdd(key, _ => new SKFont(face, primary.Size) { Subpixel = true, Edging = SKFontEdging.Antialias });
    }

    private static double Width(string line, SKFont font)
    {
        double total = 0;
        foreach (var (text, runFont) in Runs(line, font))
        {
            using var shaper = new SKShaper(runFont.Typeface);
            total += shaper.Shape(text, runFont).Width;
        }
        return total;
    }

    /// <summary>The box the text fills in capture pixels, measured from its top left corner.
    /// An empty string still gets a narrow box, so text being typed can be clicked and found.</summary>
    internal static Size Size(string text, double points, double scale)
    {
        using var font = Font(points);
        var lines = text.Split('\n');
        var widest = lines.Select(line => Width(line, font)).DefaultIfEmpty(0).Max();
        return new Size(Math.Max(widest, points / 2) * scale, LineHeight(font) * lines.Length * scale);
    }

    /// <summary>The colour a text's letters are drawn in: its own, or black or white on a filled
    /// text's box so they read on it. Public so the editor's text field types in it too.</summary>
    public static SKColor LetterColor(Style style)
    {
        var color = Palette.Color(style.ColorHex);
        return style.Filled ? MeasureShape.TextColor(color) : color;
    }

    /// <summary>How far a filled text's box reaches past its letters, in points: a third of an em
    /// at the sides, an eighth above and below, where the line's own ascent and descent already
    /// leave room.</summary>
    internal static Size BoxPadding(double points) => new(points * 0.3, points * 0.12);

    /// <summary>A filled text's box corner radius, in points.</summary>
    internal static double BoxCorner(double points) => points * 0.25;

    /// <summary>How far in from the box's left edge each line starts, in points: none when set
    /// left, half the room the line leaves when centred, all of it when set right.</summary>
    internal static double[] LineOffsets(string text, double points, TextAlign align)
    {
        using var font = Font(points);
        var widths = text.Split('\n').Select(line => Width(line, font)).ToArray();
        var box = Math.Max(widths.DefaultIfEmpty(0).Max(), points / 2);
        var share = align switch { TextAlign.Center => 0.5, TextAlign.Right => 1.0, _ => 0.0 };
        return [.. widths.Select(width => (box - width) * share)];
    }

    // ponytail: runs are laid out left to right in string order; a line mixing right-to-left
    // and left-to-right words needs ICU bidi reordering to read in the right order.
    /// <summary>Draws into a canvas whose space is capture pixels with y growing downward,
    /// with <paramref name="origin"/> as the top left corner of the first line.</summary>
    internal static void Draw(SKCanvas canvas, string text, Point origin, double points, double scale, SKColor color,
                              bool bold = false, TextAlign align = TextAlign.Left)
    {
        var offsets = align == TextAlign.Left ? [] : LineOffsets(text, points, align);
        using var font = Font(points, bold);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var ascent = -font.Metrics.Ascent;
        var height = LineHeight(font);
        canvas.Save();
        canvas.Translate((float)origin.X, (float)origin.Y);
        canvas.Scale((float)scale);
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var x = index < offsets.Length ? offsets[index] : 0;
            var baseline = ascent + index * height;
            foreach (var (run, runFont) in Runs(lines[index], font))
            {
                using var shaper = new SKShaper(runFont.Typeface);
                canvas.DrawShapedText(shaper, run, (float)x, (float)baseline, SKTextAlign.Left, runFont, paint);
                x += shaper.Shape(run, runFont).Width;
            }
        }
        canvas.Restore();
    }

    /// <summary>The width, ascent and descent of one line in capture pixels, for centring a
    /// step number and sizing a measurement's tag.</summary>
    internal static (double Width, double Ascent, double Descent) Metrics(string text, double points, double scale,
                                                                          bool bold)
    {
        using var font = Font(points, bold);
        var line = text.Split('\n')[0];
        return (Width(line, font) * scale, -font.Metrics.Ascent * scale, font.Metrics.Descent * scale);
    }
}
