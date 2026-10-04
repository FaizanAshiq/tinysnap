using System.Text.Json.Nodes;

namespace Tinysnap.Core.Tests;

/// <summary>Custom colours picked lately come back as swatches beside the eight fixed ones, so a
/// brand colour is one click away the next time.</summary>
public class RecentColorsTests
{
    [Fact]
    public void ACustomColourGoesFirstAndAFixedOneIsLeftOut()
    {
        Assert.Equal<string>(["#123ABC"], Palette.Recent("#123ABC", []));
        Assert.Equal<string>(["#123ABC"], Palette.Recent("#FF3B30", ["#123ABC"]));
    }

    [Fact]
    public void AColourPickedAgainMovesToTheFrontOnce() =>
        Assert.Equal<string>(["#123ABC", "#00FF00"], Palette.Recent("#123abc", ["#00FF00", "#123ABC"]));

    [Fact]
    public void OnlyTheLastFiveAreKept() =>
        Assert.Equal<string>(["#000006", "#000001", "#000002", "#000003", "#000004"],
                     Palette.Recent("#000006", ["#000001", "#000002", "#000003", "#000004", "#000005"]));

    [Fact]
    public void TheyAreSavedAndAFileFromBeforeHasNone()
    {
        var preferences = Preferences.Defaults with { RecentColors = ["#123ABC"] };
        Assert.Equal<string>(["#123ABC"], Preferences.FromJson((JsonObject)JsonNode.Parse(preferences.ToJson().ToJsonString())!).RecentColors);
        Assert.Empty(Preferences.FromJson((JsonObject)JsonNode.Parse("""{"colorHex":"#FF3B30"}""")!).RecentColors);
    }
}
