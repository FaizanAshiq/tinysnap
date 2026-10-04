using System.Text.Json.Nodes;

namespace Tinysnap.Core.Tests;

/// <summary>The crop can keep a ratio, 1:1, 4:3 or 16:9, for slides and posts that want one.</summary>
public class CropRatioTests
{
    private static EditorSession Session(CropRatio ratio)
    {
        var session = new EditorSession(new Document(Fixture.Capture(400, 300)), Tool.Crop);
        session.Restyle(s => s with { CropRatio = ratio });
        return session;
    }

    private static void Drag(EditorSession session, Point from, Point to)
    {
        session.PointerDown(from, reach: 4);
        session.PointerDragged(to);
        session.PointerUp();
    }

    [Fact]
    public void ANewCropKeepsTheRatio()
    {
        var session = Session(CropRatio.SixteenNine);
        Drag(session, new Point(10, 10), new Point(330, 100));
        var crop = session.Display.Crop!.Value;
        Assert.InRange(crop.Width / crop.Height, 16.0 / 9 - 0.02, 16.0 / 9 + 0.02);
        Assert.Equal(320, crop.Width);
    }

    [Fact]
    public void ADragTallerThanWideTurnsTheRatioUpright()
    {
        var session = Session(CropRatio.FourThree);
        Drag(session, new Point(10, 10), new Point(60, 210));
        var crop = session.Display.Crop!.Value;
        Assert.InRange(crop.Height / crop.Width, 4.0 / 3 - 0.02, 4.0 / 3 + 0.02);
    }

    [Fact]
    public void DraggingAnEdgeKeepsTheRatio()
    {
        var session = Session(CropRatio.FourThree);
        Drag(session, new Point(100, 60), new Point(260, 180));
        var first = session.Display.Crop!.Value;
        // The right edge, halfway down.
        Drag(session, new Point(first.MaxX, first.MidY), new Point(first.MaxX + 40, first.MidY));
        var crop = session.Display.Crop!.Value;
        Assert.True(crop.Width > first.Width);
        Assert.InRange(crop.Width / crop.Height, 4.0 / 3 - 0.02, 4.0 / 3 + 0.02);
    }

    [Fact]
    public void PickingARatioTrimsTheCropToItOnce()
    {
        var session = Session(CropRatio.Free);
        Drag(session, new Point(20, 20), new Point(320, 220));
        session.Restyle(s => s with { CropRatio = CropRatio.Square });
        Assert.Equal(new Rect(70, 20, 200, 200), session.Display.Crop);
        session.Undo();
        Assert.Equal(new Rect(20, 20, 300, 200), session.Display.Crop);
    }

    [Fact]
    public void PickingARatioWithNothingCroppedTrimsTheWholeCapture()
    {
        var session = Session(CropRatio.Free);
        session.Restyle(s => s with { CropRatio = CropRatio.SixteenNine });
        Assert.Equal(new Rect(0, 38, 400, 225), session.Display.Crop);
        session.Undo();
        Assert.Null(session.Display.Crop);
        // A capture that already has the ratio stays uncropped.
        session.Restyle(s => s with { CropRatio = CropRatio.FourThree });
        Assert.Null(session.Display.Crop);
    }

    [Fact]
    public void TheRatioIsRememberedAndAStyleFromBeforeIsFree()
    {
        var style = new Style(Palette.Red, cropRatio: CropRatio.SixteenNine);
        Assert.Equal(style, Style.FromJson(JsonNode.Parse(style.ToJson().ToJsonString())));
        Assert.Equal(CropRatio.Free, Style.FromJson(JsonNode.Parse("""{"colorHex":"#FF3B30","size":"medium"}""")).CropRatio);
        Assert.Contains("\"16:9\"", style.ToJson().ToJsonString());
        Assert.True(Tool.Crop.HasRatio());
        Assert.False(Tool.Rectangle.HasRatio());
    }
}
