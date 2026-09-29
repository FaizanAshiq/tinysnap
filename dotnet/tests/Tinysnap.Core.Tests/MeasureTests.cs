using SkiaSharp;

namespace Tinysnap.Core.Tests;

public class MeasureTests
{
    private static readonly Point From = new(50, 70);
    private static readonly Point To = new(250, 70);

    private static SKImage Render(IEnumerable<Annotation> annotations) =>
        Renderer.Render(new Document(Fixture.Capture(300, 100), annotations: [.. annotations]))!;

    [Fact]
    public void AMeasurementDrawsItsLineTicksAndLabelInItsColour()
    {
        var image = Render([Fixture.Annotation(new AnnotationKind.Measure(From, To))]);
        var tag = MeasureShape.Tag(From, To, 2, 1).Rect;
        // Along the line, clear of the tag; up a tick at the start; inside the tag's margin.
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 80, 70), (255, 59, 48)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 50, 67), (255, 59, 48)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, (int)tag.MinX + 2, 70), (255, 59, 48)));
        Assert.True(Fixture.IsClose(Fixture.Pixel(image, 80, 60), (255, 255, 255)));
    }

    [Fact]
    public void AShortMeasurementKeepsItsTagClearOfTheLineSoTheGapShows()
    {
        // A 16 point gap is narrower than its own label. Centred, the tag hid the line.
        var across = MeasureShape.Tag(new Point(50, 70), new Point(66, 70), 2, 1).Rect;
        Assert.True(across.MaxY <= 66);
        var down = MeasureShape.Tag(new Point(50, 70), new Point(50, 86), 2, 1).Rect;
        Assert.True(down.MinX >= 54);
        // A long line has room, so its tag stays on the middle of it.
        Assert.Equal(70, MeasureShape.Tag(From, To, 2, 1).Rect.MidY);
    }

    [Fact]
    public void TheLabelReadsWhiteOnADarkColourAndBlackOnALightOne()
    {
        Assert.Equal(SKColors.White, MeasureShape.TextColor(Palette.Color("#FF3B30")));
        Assert.Equal(SKColors.Black, MeasureShape.TextColor(Palette.Color("#FFCC00")));
    }

    [Fact]
    public void AMeasurementIsPickedUpByItsLineOrItsLabel()
    {
        var measure = Fixture.Annotation(new AnnotationKind.Measure(From, To));
        var tag = MeasureShape.Tag(From, To, 2, 1).Rect;
        Assert.True(measure.Contains(new Point(80, 71), 1));
        Assert.True(measure.Contains(new Point(tag.MidX, tag.MinY + 1), 1));
        Assert.False(measure.Contains(new Point(80, 90), 1));
        Assert.True(measure.Bounds(1).Contains(tag));
    }

    [Fact]
    public void KeepingAReadingAddsItsLinesUnselectedInOneStep()
    {
        var editor = new EditorSession(new Document(Fixture.Capture(300, 100)));
        editor.Choose(Tool.Measure);
        editor.Keep([new MeasureLine(From, To), new MeasureLine(new Point(150, 10), new Point(150, 90))]);
        Assert.Equal(2, editor.Display.Annotations.Length);
        Assert.Null(editor.Selection);
        editor.Undo();
        Assert.Empty(editor.Display.Annotations);
    }

    [Fact]
    public void TheMeasureToolNeverDrawsByDragging()
    {
        var editor = new EditorSession(new Document(Fixture.Capture(300, 100)));
        editor.Choose(Tool.Measure);
        editor.PointerDown(new Point(20, 20), reach: 4);
        editor.PointerDragged(new Point(200, 80));
        editor.PointerUp();
        Assert.Empty(editor.Display.Annotations);
    }

    [Fact]
    public void AClickOnNothingLetsGoOfTheSelectionSoTheNextReadingCanBeKept()
    {
        var editor = new EditorSession(new Document(Fixture.Capture(300, 100)));
        editor.Choose(Tool.Measure);
        editor.Keep([new MeasureLine(From, To)]);
        editor.PointerDown(new Point(80, 70), reach: 4);
        editor.PointerUp();
        Assert.NotNull(editor.Selection);
        editor.PointerDown(new Point(80, 20), reach: 4);
        editor.PointerUp();
        Assert.Null(editor.Selection);
    }

    /// <summary>Across and down through the middle of a card: both tags start on the same spot.</summary>
    private static readonly MeasureLine Across = new(new Point(50, 100), new Point(250, 100));
    private static readonly MeasureLine Down = new(new Point(150, 20), new Point(150, 180));

    private static Rect TagRect(MeasureLine line) => MeasureShape.Tag(line.From, line.To, 2, 1, line.LabelAt).Rect;

    /// <summary>Where a line is stroked, which no tag may sit on.</summary>
    private static Rect Stroke(MeasureLine line) =>
        new(Math.Min(line.From.X, line.To.X) - 1, Math.Min(line.From.Y, line.To.Y) - 1,
            Math.Abs(line.To.X - line.From.X) + 2, Math.Abs(line.To.Y - line.From.Y) + 2);

    [Fact]
    public void AcrossAndDownThroughTheMiddleOfACardKeepTheirTagsApart()
    {
        Assert.True(TagRect(Across).Intersects(TagRect(Down)));
        var placed = MeasureShape.ClearTags([Across, Down], 2, 1);
        Assert.False(TagRect(placed[0]).Intersects(TagRect(placed[1])));
        // Neither line strikes through the other's tag, as the Down line ran through the
        // Across tag once the tags were only kept apart from each other.
        Assert.False(TagRect(placed[0]).Intersects(Stroke(placed[1])));
        Assert.False(TagRect(placed[1]).Intersects(Stroke(placed[0])));
        // Each tag slid along its own line and stayed clear of its ticks.
        Assert.True(TagRect(placed[0]).MidY == 100 && TagRect(placed[1]).MidX == 150);
        Assert.True(TagRect(placed[0]).MinX > 50 + 4 && TagRect(placed[0]).MaxX < 250 - 4);
        Assert.True(TagRect(placed[1]).MinY > 20 + 4 && TagRect(placed[1]).MaxY < 180 - 4);
    }

    [Fact]
    public void TagsThatDoNotMeetStayOnTheirMiddles()
    {
        // Down beside the Across line's end, below it, so nothing crosses.
        var aside = new MeasureLine(new Point(240, 120), new Point(240, 180));
        Assert.Equal([Across, aside], MeasureShape.ClearTags([Across, aside], 2, 1));
    }

    [Fact]
    public void KeepingBothLinesKeepsTheirTagsApart()
    {
        var editor = new EditorSession(new Document(Fixture.Capture(300, 200)));
        editor.Choose(Tool.Measure);
        editor.Keep([Across, Down]);
        var kept = editor.Display.Annotations;
        static Rect TagOf(Annotation annotation) => annotation.Kind is AnnotationKind.Measure(var from, var to)
            ? MeasureShape.Tag(from, to, 2, 1, annotation.LabelAt).Rect
            : Rect.Null;
        Assert.True(kept.Length == 2 && !TagOf(kept[0]).Intersects(TagOf(kept[1])));
        // Picked up by its tag where the tag now is.
        Assert.True(kept[1].Contains(new Point(TagOf(kept[1]).MidX, TagOf(kept[1]).MidY), 1));
    }

    [Fact]
    public void AKeptMeasurementStretchesAndItsLabelFollows()
    {
        var measure = Fixture.Annotation(new AnnotationKind.Measure(From, To));
        var stretched = measure.Resized(Handle.End, new Point(290, 70), constrained: false);
        var kind = Assert.IsType<AnnotationKind.Measure>(stretched.Kind);
        Assert.Equal("240 pt", MeasureReading.Label(kind.From.Distance(kind.To), 1));
    }

    [Fact]
    public void WhetherATagFitsIsDecidedByTheFontsOwnWidth()
    {
        // Segoe UI is not San Francisco, so no width is hard coded: the tag's own width, from
        // TextLayout, decides a line just long enough and one just too short.
        var scale = 1.5;
        var width = 2.0;
        var probe = MeasureShape.Tag(new Point(0, 0), new Point(1000, 0), width, scale).Rect;
        var room = probe.Width + MeasureShape.TickLength(width) * scale * 2;
        var fits = MeasureShape.Tag(new Point(0, 100), new Point(room + 30, 100), width, scale).Rect;
        Assert.Equal(100, fits.MidY, 6);
        var tight = MeasureShape.Tag(new Point(0, 100), new Point(room - 30, 100), width, scale).Rect;
        Assert.True(tight.MaxY <= 100);
    }
}
