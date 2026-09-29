using System.Globalization;
using System.Text.Json.Nodes;

namespace Tinysnap.Core;

/// <summary>What the Measure tool shows and remembers: which lines, how big a change in
/// brightness counts as an edge, and whether its guide has been seen.
/// <c>EdgeContrast</c> runs 0.01 to 0.9; lower finds fainter edges.</summary>
public sealed record MeasureSettings(bool Across, bool Down, double EdgeContrast, bool GuideSeen)
{
    public static readonly MeasureSettings Defaults = new(true, false, 0.08, false);
    public const double ContrastMin = 0.01;
    public const double ContrastMax = 0.9;

    /// <summary>One percent a step, five with Shift, kept to whole percents so repeated steps
    /// never drift.</summary>
    public MeasureSettings StepContrast(bool up, bool coarse)
    {
        var step = (coarse ? 5.0 : 1.0) * (up ? 1 : -1);
        var percent = Geometry.Round(EdgeContrast * 100) + step;
        return this with { EdgeContrast = Math.Clamp(percent / 100, ContrastMin, ContrastMax) };
    }

    /// <summary>The edge contrast as the panel shows it.</summary>
    public string ContrastLabel => $"{(int)Geometry.Round(EdgeContrast * 100)}%";

    public JsonObject ToJson() => new()
    {
        ["across"] = Across,
        ["down"] = Down,
        ["edgeContrast"] = EdgeContrast,
        ["guideSeen"] = GuideSeen,
    };

    /// <summary>Each value falls back on its own, as Preferences does.</summary>
    public static MeasureSettings FromJson(JsonNode? node)
    {
        var o = node as JsonObject ?? [];
        var fallback = Defaults;
        var contrast = Json.Number(o, "edgeContrast");
        return new MeasureSettings(
            Json.Bool(o, "across") ?? fallback.Across,
            Json.Bool(o, "down") ?? fallback.Down,
            contrast is >= ContrastMin and <= ContrastMax ? contrast.Value : fallback.EdgeContrast,
            Json.Bool(o, "guideSeen") ?? fallback.GuideSeen);
    }
}

/// <summary>One line of a reading, in capture pixels. <c>LabelAt</c> is where the tag sits
/// along the line, 0 at <c>From</c> and 1 at <c>To</c>.</summary>
public sealed record MeasureLine(Point From, Point To, double LabelAt = 0.5);

public static partial class MeasureReading
{
    /// <summary>The Across and Down lines through <paramref name="point"/>, in capture pixels,
    /// for whichever are on. Empty off the capture, on a flat field, and for a span of a point
    /// or less, which is a rule rather than a space worth naming.</summary>
    public static IReadOnlyList<MeasureLine> Lines(Point point, LuminanceBuffer buffer, double scale,
                                                   MeasureSettings settings)
    {
        int x = (int)Math.Floor(point.X), y = (int)Math.Floor(point.Y);
        if (!(settings.Across || settings.Down) || x < 0 || y < 0 || x >= buffer.Width || y >= buffer.Height) return [];
        if (new EdgeDetector(settings.EdgeContrast, scale).Bounds((x, y), buffer) is not { } region) return [];
        var lines = new List<MeasureLine>();
        if (settings.Across && region.Width / scale > 1)
            lines.Add(new MeasureLine(new Point(region.MinX, point.Y), new Point(region.MaxX, point.Y)));
        if (settings.Down && region.Height / scale > 1)
            lines.Add(new MeasureLine(new Point(point.X, region.MinY), new Point(point.X, region.MaxY)));
        return lines;
    }

    /// <summary><c>16 pt</c>, <c>16.5 pt</c>: a length in points, to the half point on a
    /// capture of scale 2 or more and the whole point below that, the finest each can show.</summary>
    public static string Label(double forPixels, double scale)
    {
        var step = scale >= 2 ? 0.5 : 1;
        var points = Geometry.Round(forPixels / scale / step) * step;
        var text = points == Math.Round(points)
            ? ((long)points).ToString(CultureInfo.InvariantCulture)
            : points.ToString("0.0", CultureInfo.InvariantCulture);
        return $"{text} pt";
    }
}
