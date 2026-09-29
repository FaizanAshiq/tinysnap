namespace Tinysnap.Core.Tests;

public class MeasureReadingTests
{
    /// <summary>A capture of scale 2: two black cards 32 pixels, 16 points, apart.</summary>
    private static LuminanceBuffer TwoCards()
    {
        var image = Fixture.CaptureImage(400, 200, paint: c =>
        {
            Fixture.Fill(c, new Rect(40, 60, 100, 80), Fixture.Black);
            Fixture.Fill(c, new Rect(172, 60, 100, 80), Fixture.Black);
        });
        return LuminanceBuffer.From(image)!;
    }

    private static MeasureSettings Settings(bool across = true, bool down = false) =>
        MeasureSettings.Defaults with { Across = across, Down = down };

    [Fact]
    public void AcrossSpansTheGapThroughThePointer()
    {
        var lines = MeasureReading.Lines(new Point(156, 100), TwoCards(), 2, Settings());
        Assert.Equal([new MeasureLine(new Point(140, 100), new Point(172, 100))], lines);
        Assert.Equal("16 pt", MeasureReading.Label(32, 2));
    }

    [Fact]
    public void BothOnGiveAcrossThenDown()
    {
        // Inside the first card: 100 by 80 pixels, 50 by 40 points.
        var lines = MeasureReading.Lines(new Point(90, 100), TwoCards(), 2, Settings(across: true, down: true));
        Assert.Equal(
        [
            new MeasureLine(new Point(40, 100), new Point(140, 100)),
            new MeasureLine(new Point(90, 60), new Point(90, 140)),
        ], lines);
    }

    [Fact]
    public void NothingIsShownWithBothOffOrOffTheCapture()
    {
        var buffer = TwoCards();
        Assert.Empty(MeasureReading.Lines(new Point(156, 100), buffer, 2, Settings(across: false)));
        Assert.Empty(MeasureReading.Lines(new Point(-5, 100), buffer, 2, Settings()));
        Assert.Empty(MeasureReading.Lines(new Point(156, 250), buffer, 2, Settings()));
    }

    [Fact]
    public void ASpanOfAPointOrLessIsARuleNotAGap()
    {
        // Two pixels apart on a capture of scale 2: one point.
        var image = Fixture.CaptureImage(100, 40, paint: c =>
        {
            Fixture.Fill(c, new Rect(10, 0, 40, 40), Fixture.Black);
            Fixture.Fill(c, new Rect(52, 0, 40, 40), Fixture.Black);
        });
        Assert.Empty(MeasureReading.Lines(new Point(50, 20), LuminanceBuffer.From(image)!, 2, Settings()));
    }

    [Fact]
    public void LengthsReadInPointsToTheFinestStepTheCaptureShows()
    {
        Assert.Equal("16.5 pt", MeasureReading.Label(33, 2));
        Assert.Equal("16 pt", MeasureReading.Label(16, 1));
        Assert.Equal("33 pt", MeasureReading.Label(33, 1));
        Assert.Equal("600 pt", MeasureReading.Label(1200, 2));
    }

    [Fact]
    public void TheEdgeContrastStepsInWholePercentsWithinItsRange()
    {
        var settings = MeasureSettings.Defaults;
        for (var i = 0; i < 6; i++) settings = settings.StepContrast(up: false, coarse: false);
        Assert.Equal("2%", settings.ContrastLabel);
        settings = settings.StepContrast(up: false, coarse: true);
        Assert.Equal(MeasureSettings.ContrastMin, settings.EdgeContrast);
        for (var i = 0; i < 30; i++) settings = settings.StepContrast(up: true, coarse: true);
        Assert.Equal(MeasureSettings.ContrastMax, settings.EdgeContrast);
    }

    [Fact]
    public void LengthsAtOneAndAHalfReadInWholePoints()
    {
        // Only a scale of 2 or more can show half points.
        Assert.Equal("200 pt", MeasureReading.Label(300, 1.5));
        Assert.Equal("67 pt", MeasureReading.Label(100, 1.5));
    }

    [Fact]
    public void TheSettingsSaveAndEachBadValueFallsBackAlone()
    {
        var settings = new MeasureSettings(false, true, 0.03, true);
        Assert.Equal(settings, MeasureSettings.FromJson(Json.Parse(Json.Write(settings.ToJson()))));
        var bad = MeasureSettings.FromJson(Json.Parse("""{"down": true, "edgeContrast": 4, "across": "yes"}"""));
        Assert.Equal(new MeasureSettings(true, true, 0.08, false), bad);
    }
}
