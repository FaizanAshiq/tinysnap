namespace Tinysnap.Core.Tests;

public class StyleTests
{
    private static Style RoundTrip(Style style) => Style.FromJson(Json.Parse(Json.Write(style.ToJson())));

    private static Style Decode(string json) => Style.FromJson(Json.Parse(json));

    [Fact]
    public void AnOverlaysOpacityAndDifferenceSurviveSaving()
    {
        var style = new Style("#FF3B30", corners: CornerSize.Square, opacity: 0.5, difference: true);
        Assert.Equal(style, RoundTrip(style));
    }

    [Fact]
    public void ReadsHexColours()
    {
        Assert.Equal(1, Palette.Components("#FF8000")?.Red);
        Assert.Equal(1, Palette.Components("00FF00")?.Green);
        Assert.Null(Palette.Components("#FF80"));
        Assert.Null(Palette.Components("#GG0000"));
        Assert.Null(Palette.Components("+FFFFF"));
    }

    [Fact]
    public void WritesHexBack()
    {
        Assert.Equal("#FF8000", Palette.Hex(1, 0.5, 0));
    }

    [Fact]
    public void FindsToolsByLetterInEitherCase()
    {
        Assert.Equal(Tool.Arrow, ToolInfo.ForKey('a'));
        Assert.Equal(Tool.Erase, ToolInfo.ForKey('E'));
        Assert.Null(ToolInfo.ForKey('z'));
    }

    [Fact]
    public void EveryToolHasItsOwnLetter()
    {
        var tools = Enum.GetValues<Tool>();
        Assert.Equal(tools.Length, tools.Select(t => t.Key()).Distinct().Count());
    }

    [Fact]
    public void SizesComeFromEachToolsTable()
    {
        Assert.Equal(4, Tool.Arrow.Points(StyleSize.Medium));
        Assert.Equal(9, Tool.Arrow.Points(StyleSize.ExtraLarge));
        Assert.Equal(28, Tool.Text.Points(StyleSize.Large));
        Assert.Null(Tool.Crop.Points(StyleSize.Medium));
    }

    [Fact]
    public void ThereAreFiveSizesAndStepsStopAtTheEnds()
    {
        Assert.Equal(5, Enum.GetValues<StyleSize>().Length);
        Assert.Equal(StyleSize.Large, StyleSize.Medium.Thicker());
        Assert.Equal(StyleSize.Small, StyleSize.Medium.Thinner());
        Assert.Equal(StyleSize.ExtraLarge, StyleSize.ExtraLarge.Thicker());
        Assert.Equal(StyleSize.ExtraSmall, StyleSize.ExtraSmall.Thinner());
    }

    [Fact]
    public void EachBoxToolStartsWithItsOwnCorners()
    {
        Assert.Equal(CornerSize.Medium, Tool.Rectangle.DefaultStyle().Corners);
        Assert.Equal(CornerSize.Small, Tool.Spotlight.DefaultStyle().Corners);
        Assert.Equal(CornerSize.Square, Tool.Image.DefaultStyle().Corners);
        Assert.True(Tool.Rectangle.HasCorners() && Tool.Spotlight.HasCorners() && Tool.Blur.HasCorners());
        Assert.True(!Tool.Erase.HasCorners() && !Tool.Arrow.HasCorners());
    }

    [Fact]
    public void CornersSaveAndAnOldSquareSettingStillLoads()
    {
        Assert.Equal(CornerSize.Square, Decode("""{"colorHex": "#FF3B30", "sharpCorners": true}""").Corners);
        var style = new Style(Palette.Red, corners: CornerSize.Full);
        Assert.Equal(style, RoundTrip(style));
    }

    [Fact]
    public void ABadStyleValueFallsBackOnItsOwn()
    {
        // The overlay settings join the rest: missing or bad, each falls back alone.
        var old = Decode("""{"colorHex": "#FF3B30"}""");
        Assert.True(old.Opacity == 1 && !old.Difference);
        var bad = Decode("""{"opacity": 7, "difference": "yes"}""");
        Assert.True(bad.Opacity == 1 && !bad.Difference);
        var faint = Decode("""{"opacity": 0.01}""");
        Assert.Equal(0.1, faint.Opacity);

        var style = Decode("""{"colorHex": "red", "size": "huge", "filled": true}""");
        Assert.Equal(new Style(Palette.Red, StyleSize.Medium, filled: true), style);
    }
}
