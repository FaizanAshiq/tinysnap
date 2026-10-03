using System.Collections.Immutable;
using SkiaSharp;

namespace Tinysnap.Core;

/// <summary>What text recognition found in an image: QR code contents, and lines of text top
/// to bottom.</summary>
public sealed record TextReading(ImmutableArray<string> Codes, ImmutableArray<string> Lines)
{
    public bool Equals(TextReading? other) =>
        other is not null && Codes.SequenceEqual(other.Codes) && Lines.SequenceEqual(other.Lines);

    public override int GetHashCode() => HashCode.Combine(Codes.Length, Lines.Length);

    public bool IsEmpty => Codes.IsEmpty && Lines.IsEmpty;

    /// <summary>What is copied: a reading for text holds only lines, and a scan only codes.</summary>
    public string Text => string.Join("\n", Codes.IsEmpty ? Lines : Codes);

    /// <summary>A web address to offer Open Link for, only when that is all the result is.</summary>
    public Uri? Link
    {
        get
        {
            var candidate = Text.Trim();
            if (candidate.Any(char.IsWhiteSpace) || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
            return uri.Scheme is "http" or "https" && uri.Host.Length > 0 ? uri : null;
        }
    }
}

/// <summary>The parts of reading text that need no recognition engine. The engine itself is
/// the platform's, added with the Windows app.</summary>
public static class TextReader
{
    /// <summary>Recognisers report text in blocks, which reads a dashboard column by column.
    /// Lines are put into rows instead: a row is every line whose middle sits within half a
    /// line of the row's first. Rows go top to bottom, and each row left to right. Boxes are
    /// normalised with y growing upward, as Vision reports them.</summary>
    internal static int[] ReadingOrder(IReadOnlyList<Rect> boxes)
    {
        var rows = new List<List<int>>();
        foreach (var index in Enumerable.Range(0, boxes.Count).OrderByDescending(i => boxes[i].MidY))
        {
            if (rows.Count > 0 && Math.Abs(boxes[rows[^1][0]].MidY - boxes[index].MidY) < boxes[rows[^1][0]].Size.Height / 2)
                rows[^1].Add(index);
            else
                rows.Add([index]);
        }
        return [.. rows.SelectMany(row => row.OrderBy(i => boxes[i].MinX))];
    }

    /// <summary>A recogniser's lines in reading order, for boxes in pixels with y growing
    /// downward, as Windows reports them.</summary>
    public static ImmutableArray<string> InReadingOrder(IReadOnlyList<(string Text, Rect Box)> lines) =>
        [.. ReadingOrder([.. lines.Select(line => new Rect(line.Box.X, -line.Box.Y - line.Box.Height, line.Box.Width, line.Box.Height))])
            .Select(index => lines[index].Text)];

    /// <summary>Black words on white, as a capture would have them, read once at launch so the
    /// first Copy Text finds the recogniser loaded. A blank image would not do: with nothing in
    /// it to read, the recogniser never loads its language data.</summary>
    public static SKImage WarmUpSample()
    {
        using var surface = SKSurface.Create(new SKImageInfo(480, 80));
        surface.Canvas.Clear(SKColors.White);
        TextLayout.Draw(surface.Canvas, "Tinysnap reads text", new Point(20, 20), 18, 2, SKColors.Black);
        return surface.Snapshot();
    }

    /// <summary>Join Lines: every run of spaces and line breaks becomes one space.</summary>
    public static string Join(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
