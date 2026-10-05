namespace Tinysnap.Core.Tests;

public class DocumentTests
{
    [Fact]
    public void TheExtentIsTheCaptureWhileEverythingIsInsideIt()
    {
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(40, 20, 50, 30)));
        var document = new Document(Fixture.Capture(200, 100, 2), annotations: [box]);
        Assert.Equal(new Rect(0, 0, 200, 100), document.Extent);
        Assert.Equal(document.Extent, document.OutputRect);
    }

    [Fact]
    public void AShapePastTheEdgeGrowsTheExtentByItsBoundsAndAMargin()
    {
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(-50, 60, 100, 80)));
        var document = new Document(Fixture.Capture(200, 100, 2), annotations: [box]);
        // 16 points of margin is 32 pixels on a 2x capture.
        var grown = box.Bounds(2).Inset(-32, -32);
        Assert.Equal(new Rect(0, 0, 200, 100).Union(grown).Integral, document.Extent);
        Assert.True(document.Extent.MinX < 0 && document.Extent.MaxY > 100);
        Assert.True(document.Extent.MaxX == 200 && document.Extent.MinY == 0);
    }

    /// <summary>A measurement read from edge to edge, as across a whole capture whose card is too
    /// faint to stop at, ends on the edges; its end ticks overhang them, and grew the canvas.</summary>
    [Fact]
    public void AMeasurementToTheEdgesGrowsNothing()
    {
        var across = Fixture.Annotation(new AnnotationKind.Measure(new Point(0, 50), new Point(200, 50)));
        var down = Fixture.Annotation(new AnnotationKind.Measure(new Point(120, 0), new Point(120, 100)));
        var document = new Document(Fixture.Capture(200, 100, 2), annotations: [across, down]);
        Assert.Equal(new Rect(0, 0, 200, 100), document.Extent);
        // One that goes past an edge still grows it.
        var past = Fixture.Annotation(new AnnotationKind.Measure(new Point(120, -40), new Point(120, 100)));
        Assert.True(new Document(Fixture.Capture(200, 100, 2), annotations: [past]).Extent.MinY < 0);
    }

    [Fact]
    public void ACropStillDecidesWhatIsOutputOnAGrownCanvas()
    {
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(150, 10, 100, 20)));
        var document = new Document(Fixture.Capture(200, 100), annotations: [box]) with { Crop = new Rect(10, 10, 50, 50) };
        Assert.Equal(new Rect(10, 10, 50, 50), document.OutputRect);
    }

    [Fact]
    public void StepsNumberThemselvesInOrderAndRenumberOnDelete()
    {
        var steps = Enumerable.Range(0, 3).Select(i => Fixture.Annotation(new AnnotationKind.Step(new Point(i * 20, 10)))).ToArray();
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(0, 0, 10, 10)));
        var document = new Document(Fixture.Capture(200, 100), annotations: [steps[0], box, steps[1], steps[2]]);

        Assert.Equal(3, document.StepNumber(steps[2].Id));
        document = document.Removing(steps[1].Id);
        Assert.Equal(2, document.StepNumber(steps[2].Id));
        Assert.Null(document.StepNumber(box.Id));
    }

    [Fact]
    public void FindsTheTopmostAnnotationUnderAPoint()
    {
        var filled = new Style(Palette.Red, filled: true);
        var below = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(0, 0, 50, 50)), filled);
        var above = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(20, 20, 50, 50)), filled);
        var document = new Document(Fixture.Capture(200, 100), annotations: [below, above]);

        Assert.Equal(above.Id, document.Topmost(new Point(30, 30)));
        Assert.Equal(below.Id, document.Topmost(new Point(5, 5)));
        Assert.Null(document.Topmost(new Point(150, 90)));
    }

    [Fact]
    public void CropsAFrozenDisplayFromPointsToPixels()
    {
        var display = Fixture.Capture(200, 100, 2).Image;
        var capture = Capture.Crop(display, new Rect(10, 5, 20, 10), 2);
        Assert.NotNull(capture);
        Assert.Equal(new Size(40, 20), capture.PixelSize);
        Assert.Equal(new Size(20, 10), capture.PointSize);
        Assert.Equal(new Size(20, 20), Capture.Crop(display, new Rect(90, 40, 50, 50), 2)?.PixelSize);
        Assert.Null(Capture.Crop(display, new Rect(500, 0, 10, 10), 2));
    }

    [Fact]
    public void CropsAStandardDisplayOnePixelPerPoint()
    {
        var display = Fixture.Capture(200, 100, 1).Image;
        var capture = Capture.Crop(display, new Rect(10, 5, 20, 10), 1);
        Assert.NotNull(capture);
        Assert.Equal(new Size(20, 10), capture.PixelSize);
    }

    [Fact]
    public void TheOutputIsTheCropWhenThereIsOne()
    {
        var document = new Document(Fixture.Capture(200, 100));
        Assert.Equal(new Rect(0, 0, 200, 100), document.OutputRect);
        document = document with { Crop = new Rect(10, 10, 50, 20) };
        Assert.Equal(new Rect(10, 10, 50, 20), document.OutputRect);
    }

    [Fact]
    public void CropsADisplayAtOneAndAHalfFromPointsToWholePixels()
    {
        var screen = Fixture.CaptureImage(300, 150);
        var capture = Capture.Crop(screen, new Rect(10, 10, 100, 50), 1.5);
        Assert.NotNull(capture);
        Assert.Equal(1.5, capture.Scale);
        Assert.Equal(new Size(150, 75), capture.PixelSize);
        Assert.Equal(new Size(100, 50), capture.PointSize);
    }

    [Fact]
    public void TwoDocumentsWithTheSameContentsAreEqual()
    {
        // Undo compares documents; separate but equal annotation lists must match.
        var capture = Fixture.Capture(10, 10);
        var line = Fixture.Annotation(new AnnotationKind.Freehand([new Point(1, 1), new Point(2, 2)]));
        Assert.Equal(new Document(capture, annotations: [line]), new Document(capture, annotations: [line with { }]));
        Assert.Equal(new AnnotationKind.Freehand([new Point(1, 1)]), new AnnotationKind.Freehand([new Point(1, 1)]));
    }
}
