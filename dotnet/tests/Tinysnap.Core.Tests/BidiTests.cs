namespace Tinysnap.Core.Tests;

/// <summary>A line mixing Urdu or Arabic with English reads in the order the Unicode bidi rules
/// give it, the order Core Text draws it in on the Mac.</summary>
public class BidiTests
{
    private const string Salam = "سلام";
    private const string Dunya = "دنیا";

    [Fact]
    public void AnEnglishLineRaisesOnlyItsUrduWord()
    {
        Assert.Equal([0, 0, 0, 1, 1, 1, 1, 0, 0, 0], Bidi.Levels($"ab {Salam} cd"));
    }

    [Fact]
    public void AnUrduLineRunsRightToLeftAndRaisesItsEnglish()
    {
        Assert.Equal([1, 1, 1, 1, 1, 2, 2, 1, 1, 1, 1, 1], Bidi.Levels($"{Salam} ab {Dunya}"));
    }

    [Fact]
    public void ANumberReadsLeftToRightAndBelongsToTheTextBeforeIt()
    {
        Assert.All(Bidi.Levels("Price 250"), level => Assert.Equal(0, level));
        Assert.Equal([0, 0, 0, 1, 1, 1, 1, 1, 2, 2], Bidi.Levels($"ab {Salam} 25"));
        Assert.Equal([1, 1, 1, 1, 1, 2, 2, 1, 1, 1, 1, 1], Bidi.Levels($"{Salam} 25 {Dunya}"));
    }

    [Fact]
    public void AMarkAndUrduPunctuationGoWithTheUrdu()
    {
        // A zabar over the seen, and the Urdu question mark at the end of an English line.
        Assert.All(Bidi.Levels("سَلام"), level => Assert.Equal(1, level));
        Assert.Equal([0, 0, 0, 1, 1, 1, 1, 1], Bidi.Levels($"ab {Salam}؟"));
    }

    [Fact]
    public void RunsAreTurnedRoundFromTheHighestLevelDown()
    {
        int[] Order(params int[] levels) =>
            [.. Bidi.Reorder(levels.Select((level, index) => (Level: level, Index: index)).ToList(), run => run.Level)
                .Select(run => run.Index)];
        Assert.Equal([0, 2, 1, 3], Order(0, 1, 1, 0));
        Assert.Equal([3, 1, 2, 0], Order(1, 2, 2, 1));
        Assert.Equal([0, 1], Order(0, 0));
    }

    /// <summary>Where each word lands among the runs drawn left to right. Two words in one run
    /// share an index, and the shaper puts them in order inside it.</summary>
    private static int Drawn(string line, string word)
    {
        using var font = TextLayout.Font(20);
        return TextLayout.VisualRuns(line, font).FindIndex(run => run.Text.Contains(word));
    }

    [Fact]
    public void TwoUrduWordsInAnEnglishLineAreDrawnRightToLeft()
    {
        var line = $"ab {Salam} {Dunya}";
        Assert.True(Drawn(line, Dunya) <= Drawn(line, Salam));
        Assert.True(Drawn(line, "ab") < Drawn(line, Dunya));
    }

    [Fact]
    public void AnUrduLineIsDrawnFromItsLastWordOnTheLeft()
    {
        var line = $"{Salam} ab {Dunya}";
        Assert.True(Drawn(line, Dunya) < Drawn(line, "ab"));
        Assert.True(Drawn(line, "ab") < Drawn(line, Salam));
    }
}
