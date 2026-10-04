using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Buffer = HarfBuzzSharp.Buffer;

namespace Tinysnap.Core;

/// <summary>Shapes a run with HarfBuzz at the weight and width Skia draws it in, then draws it.
///
/// SkiaSharp's own shaper reads a variable font at its default weight, and the system fonts on
/// a Mac, on Windows 11 and on Ubuntu are all variable, so bold letters were drawn bold but
/// spaced as regular ones: bold text was measured too narrow and its letters crowded. Each face
/// gets one HarfBuzz font for the life of the app, set to that face's weight and width.</summary>
// ponytail: weight and width only; an optical size axis stays at the font's default, which
// matters only if a font's spacing changes with it.
internal static class Shaping
{
    /// <summary>HarfBuzz lays out in this many units per em, scaled to the font's size after.</summary>
    private const int UnitsPerEm = 512;

    private static readonly ConcurrentDictionary<(string Family, int Weight, int Width, SKFontStyleSlant Slant), Lazy<Font>> Fonts = new();

    internal readonly record struct Run(ushort[] Glyphs, SKPoint[] Points, float Width);

    /// <summary>The glyphs of <paramref name="text"/> and where each sits, from the run's start, in
    /// <paramref name="font"/>'s size.</summary>
    internal static Run Shape(string text, SKFont font)
    {
        var typeface = font.Typeface;
        var harfBuzz = Fonts.GetOrAdd((typeface.FamilyName, typeface.FontWeight, typeface.FontWidth, typeface.FontSlant),
                                      _ => new Lazy<Font>(() => Make(typeface))).Value;
        using var buffer = new Buffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();
        harfBuzz.Shape(buffer);
        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        var scale = font.Size / UnitsPerEm;
        var glyphs = new ushort[infos.Length];
        var points = new SKPoint[infos.Length];
        float x = 0, y = 0;
        for (var index = 0; index < infos.Length; index++)
        {
            glyphs[index] = (ushort)infos[index].Codepoint;
            points[index] = new SKPoint(x + positions[index].XOffset * scale, y - positions[index].YOffset * scale);
            x += positions[index].XAdvance * scale;
            y += positions[index].YAdvance * scale;
        }
        return new Run(glyphs, points, x);
    }

    /// <summary>Draws <paramref name="text"/> with its baseline starting at <paramref name="x"/>,
    /// <paramref name="y"/>, and returns how far it reaches.</summary>
    internal static float Draw(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint)
    {
        var run = Shape(text, font);
        if (run.Glyphs.Length == 0) return run.Width;
        using var builder = new SKTextBlobBuilder();
        var buffer = builder.AllocatePositionedRun(font, run.Glyphs.Length);
        buffer.SetGlyphs(run.Glyphs);
        buffer.SetPositions(run.Points);
        using var blob = builder.Build();
        if (blob is not null) canvas.DrawText(blob, x, y, paint);
        return run.Width;
    }

    private static Font Make(SKTypeface typeface)
    {
        using var blob = typeface.OpenStream(out var index).ToHarfBuzzBlob();
        using var face = new Face(blob, index) { Index = index, UnitsPerEm = typeface.UnitsPerEm };
        var font = new Font(face);
        font.SetScale(UnitsPerEm, UnitsPerEm);
        font.SetFunctionsOpenType();
        // A font without these axes ignores them, so a face with no variations is untouched.
        Variation[] variations =
        [
            new((uint)Tag.Parse("wght"), typeface.FontWeight),
            new((uint)Tag.Parse("wdth"), WidthPercent(typeface.FontWidth)),
        ];
        hb_font_set_variations(font.Handle, variations, (uint)variations.Length);
        return font;
    }

    /// <summary>The OpenType width classes 1 to 9 as the percentages the width axis takes.</summary>
    private static float WidthPercent(int width) =>
        width switch { 1 => 50, 2 => 62.5f, 3 => 75, 4 => 87.5f, 6 => 112.5f, 7 => 125, 8 => 150, 9 => 200, _ => 100 };

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Variation(uint Tag, float Value);

    // Exported by the HarfBuzz library HarfBuzzSharp loads, which does not wrap it.
    [DllImport("libHarfBuzzSharp", CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_font_set_variations(IntPtr font, Variation[] variations, uint length);
}
