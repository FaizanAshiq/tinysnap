using System.Collections.Immutable;

namespace Tinysnap.Core.Tests;

public class LayerTests
{
    private static Document Doc(params Annotation[] annotations) =>
        new(Fixture.Capture(400, 300, 2), annotations: [.. annotations]);

    [Fact]
    public void AHiddenShapeIsNotDrawnClickedOrBordered()
    {
        var box = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(100, 100, 80, 60)), new Style(Palette.Red, filled: true))
            with { IsHidden = true };
        var doc = Doc(box);
        Assert.Null(doc.Topmost(new Point(140, 130)));
        Assert.Null(doc.PickUp(new Point(140, 130), 8));
        Assert.Null(doc.BorderHit(new Point(96, 130), 8));
        var blank = Renderer.Render(Doc())!;
        var hidden = Renderer.Render(doc)!;
        Assert.Equal(Fixture.Pixel(blank, 140, 130), Fixture.Pixel(hidden, 140, 130));
    }

    [Fact]
    public void ALockedShapeIsSelectableButNotPickedUpByADrawingTool()
    {
        var blur = Fixture.Annotation(new AnnotationKind.Blur(new Rect(100, 100, 80, 60))) with { IsLocked = true };
        var doc = Doc(blur);
        Assert.Equal(blur.Id, doc.Topmost(new Point(140, 130)));
        Assert.Null(doc.PickUp(new Point(140, 130), 8));
        Assert.Null(doc.PickUp(new Point(96, 130), 8));
    }

    [Fact]
    public void AHiddenShapePastTheEdgeDoesNotGrowTheCanvas()
    {
        var far = Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(700, 100, 80, 60))) with { IsHidden = true };
        Assert.Equal(new Rect(0, 0, 400, 300), Doc(far).Extent);
    }

    [Fact]
    public void LayerNamesSayWhatEachShapeIs()
    {
        var text = Fixture.Annotation(new AnnotationKind.Text(new Point(10, 10), "Best week so far\nsecond line"));
        var first = Fixture.Annotation(new AnnotationKind.Step(new Point(50, 50)));
        var hiddenStep = Fixture.Annotation(new AnnotationKind.Step(new Point(80, 50))) with { IsHidden = true };
        var second = Fixture.Annotation(new AnnotationKind.Step(new Point(110, 50)));
        var blur = Fixture.Annotation(new AnnotationKind.Blur(new Rect(0, 0, 20, 20)));
        var arrow = Fixture.Annotation(new AnnotationKind.Arrow(new Point(0, 0), new Point(20, 20)));
        var doc = Doc(text, first, hiddenStep, second, blur, arrow);
        Assert.Equal("Best week so far", doc.LayerName(text.Id));
        Assert.Equal("Step 1", doc.LayerName(first.Id));
        Assert.Equal("Step 2", doc.LayerName(second.Id));
        Assert.Equal("Step", doc.LayerName(hiddenStep.Id));
        Assert.Equal("Blur", doc.LayerName(blur.Id));
        Assert.Equal("Arrow", doc.LayerName(arrow.Id));
    }
}
