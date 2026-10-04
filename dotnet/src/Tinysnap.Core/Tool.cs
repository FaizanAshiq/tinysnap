namespace Tinysnap.Core;

public enum Tool
{
    Select, Arrow, Line, Rectangle, Oval, Text, Highlighter, Freehand,
    Step, Spotlight, Magnifier, Image, Crop, Blur, Pixelate, Erase, Measure,
}

public static class ToolInfo
{
    /// <summary>A new magnifier is 160 points across.</summary>
    public const double MagnifierRadiusPoints = 80;

    public static char Key(this Tool tool) => tool switch
    {
        Tool.Select => 'v',
        Tool.Arrow => 'a',
        Tool.Line => 'l',
        Tool.Rectangle => 'r',
        Tool.Oval => 'o',
        Tool.Text => 't',
        Tool.Highlighter => 'h',
        Tool.Freehand => 'f',
        Tool.Step => 'n',
        Tool.Spotlight => 's',
        Tool.Magnifier => 'm',
        Tool.Image => 'i',
        Tool.Crop => 'c',
        Tool.Blur => 'b',
        Tool.Pixelate => 'p',
        Tool.Erase => 'e',
        _ => 'd',
    };

    public static Tool? ForKey(char character)
    {
        var lowered = char.ToLowerInvariant(character);
        return Enum.GetValues<Tool>().Cast<Tool?>().FirstOrDefault(tool => tool!.Value.Key() == lowered);
    }

    public static string Title(this Tool tool) => tool switch
    {
        Tool.Select => "Select",
        Tool.Arrow => "Arrow",
        Tool.Line => "Line",
        Tool.Rectangle => "Rectangle",
        Tool.Oval => "Oval",
        Tool.Text => "Text",
        Tool.Highlighter => "Highlighter",
        Tool.Freehand => "Freehand",
        Tool.Step => "Step Number",
        Tool.Spotlight => "Spotlight",
        Tool.Magnifier => "Magnifier",
        Tool.Image => "Image",
        Tool.Crop => "Crop",
        Tool.Blur => "Blur, can be partly reversed",
        Tool.Pixelate => "Pixelate, can be partly reversed",
        Tool.Erase => "Erase, the only guaranteed redaction",
        _ => "Measure",
    };

    /// <summary>The five sizes in points, thinnest first, or null for a tool without a size.</summary>
    private static double[]? SizeTable(Tool tool) => tool switch
    {
        Tool.Arrow or Tool.Line or Tool.Rectangle or Tool.Oval or Tool.Freehand => [2, 3, 4, 6, 9],
        Tool.Text => [12, 16, 20, 28, 40],
        Tool.Highlighter => [10, 14, 20, 26, 34],
        Tool.Step => [20, 26, 32, 40, 50],
        Tool.Blur => [3, 5, 8, 12, 18],
        Tool.Pixelate => [6, 8, 12, 16, 24],
        Tool.Measure => [1, 1.5, 2, 3, 4],
        _ => null,
    };

    public static double? Points(this Tool tool, StyleSize size) => SizeTable(tool)?[(int)size];

    public static bool HasSize(this Tool tool) => SizeTable(tool) is not null;

    public static bool HasColor(this Tool tool) => tool is Tool.Arrow or Tool.Line or Tool.Rectangle or Tool.Oval
        or Tool.Text or Tool.Highlighter or Tool.Freehand or Tool.Step or Tool.Measure;

    /// <summary>Boxes and ovals filled instead of outlined, and text set on a box in its colour.</summary>
    public static bool HasFill(this Tool tool) => tool is Tool.Rectangle or Tool.Oval or Tool.Text;

    /// <summary>Text only: its lines set left, centred or right.</summary>
    public static bool HasAlign(this Tool tool) => tool is Tool.Text;

    /// <summary>Text only: set in the bold face.</summary>
    public static bool HasBold(this Tool tool) => tool is Tool.Text;

    /// <summary>Lines, boxes and ovals: drawn in dashes. A filled box or oval has no outline to dash.</summary>
    public static bool HasDash(this Tool tool) => tool is Tool.Line or Tool.Rectangle or Tool.Oval;

    /// <summary>Steps only: numbers or letters, from a start the capture keeps.</summary>
    public static bool HasCounter(this Tool tool) => tool is Tool.Step;

    /// <summary>The highlighter only: a straight stroke, or one that follows the pointer.</summary>
    public static bool HasFreehand(this Tool tool) => tool is Tool.Highlighter;

    /// <summary>The spotlight only: dim what is outside it, or blur it.</summary>
    public static bool HasOutsideBlur(this Tool tool) => tool is Tool.Spotlight;

    /// <summary>The crop only: any shape, or a ratio it keeps.</summary>
    public static bool HasRatio(this Tool tool) => tool is Tool.Crop;

    /// <summary>Erase only: cover every email, phone number, number or line of text the reader finds.</summary>
    public static bool HasRedact(this Tool tool) => tool is Tool.Erase;

    /// <summary>Pasted images: opacity and the difference blend, for comparing against the capture.</summary>
    public static bool HasOverlay(this Tool tool) => tool is Tool.Image;

    /// <summary>Pasted images, boxes and ovals: see-through, so a filled box tints rather than hides.</summary>
    public static bool HasOpacity(this Tool tool) => tool is Tool.Image or Tool.Rectangle or Tool.Oval;

    /// <summary>Boxes whose corner radius can be set. Not erase: rounding it would leave the
    /// corners of what it hides showing.</summary>
    public static bool HasCorners(this Tool tool) =>
        tool is Tool.Rectangle or Tool.Spotlight or Tool.Blur or Tool.Pixelate or Tool.Image;

    public static Style DefaultStyle(this Tool tool)
    {
        var corners = tool switch
        {
            Tool.Image => CornerSize.Square,
            Tool.Spotlight or Tool.Blur or Tool.Pixelate => CornerSize.Small,
            _ => CornerSize.Medium,
        };
        return new Style(Palette.Red, corners: corners);
    }
}
