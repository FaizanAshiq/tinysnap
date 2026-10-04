namespace Tinysnap.Core.Tests;

/// <summary>Redact all text: the reader's words, with their boxes, matched for emails, phone
/// numbers, numbers or everything, and each match covered by an erase box.</summary>
public class TextRedactionTests
{
    /// <summary>A line of words 10 pixels apart, each 8 wide per letter, on row <paramref name="y"/>.</summary>
    private static TextLine Line(string[] words, double y)
    {
        var x = 10.0;
        var list = new List<TextWord>();
        foreach (var text in words)
        {
            list.Add(new TextWord(text, new Rect(x, y, text.Length * 8, 16)));
            x += text.Length * 8 + 10;
        }
        return new TextLine([.. list]);
    }

    private static readonly TextLine[] Page =
    [
        Line(["Email", "marcus.reyes@example.com"], 10),
        Line(["Phone", "+1", "(555)", "014-2297"], 40),
        Line(["Order", "#10482", "total", "$129.00"], 70),
        Line(["Prefers", "delivery", "after", "5", "pm."], 100),
    ];

    [Fact]
    public void AnEmailIsCoveredAndNothingElse() =>
        Assert.Equal([Page[0].Words[1].Box], TextRedaction.Boxes(Page, RedactTarget.Emails));

    [Fact]
    public void APhoneNumberSplitOverWordsIsCoveredWhole()
    {
        var words = Page[1].Words;
        Assert.Equal([words[1].Box.Union(words[2].Box).Union(words[3].Box)], TextRedaction.Boxes(Page, RedactTarget.Phones));
    }

    [Fact]
    public void NumbersCoverRunsOfFourDigitsOrMore()
    {
        var boxes = TextRedaction.Boxes(Page, RedactTarget.Numbers);
        // The phone, the order number and the total; not the lone 5.
        Assert.Equal(3, boxes.Count);
        Assert.Contains(Page[2].Words[1].Box, boxes);
        Assert.DoesNotContain(boxes, box => box.Intersects(Page[3].Words[3].Box));
    }

    [Fact]
    public void AllTextCoversEveryLine()
    {
        var boxes = TextRedaction.Boxes(Page, RedactTarget.AllText);
        Assert.Equal(4, boxes.Count);
        Assert.Equal(Page[3].Words.Skip(1).Aggregate(Page[3].Words[0].Box, (all, word) => all.Union(word.Box)), boxes[3]);
    }

    [Fact]
    public void RedactingAddsAnEraseBoxForEachMatchAsOneStep()
    {
        var session = new EditorSession(new Document(Fixture.Capture(400, 200)), Tool.Erase);
        var boxes = TextRedaction.Boxes(Page, RedactTarget.Numbers);
        session.Redact(boxes);
        Assert.Equal(3, session.Display.Annotations.Length);
        Assert.All(session.Display.Annotations, a => Assert.Equal(Tool.Erase, a.Tool));
        // Each box reaches a little past its text, so no edge of a letter shows.
        Assert.True(((AnnotationKind.Erase)session.Display.Annotations[0].Kind).Rect.Contains(boxes[0]));
        session.Undo();
        Assert.Empty(session.Display.Annotations);
    }
}
