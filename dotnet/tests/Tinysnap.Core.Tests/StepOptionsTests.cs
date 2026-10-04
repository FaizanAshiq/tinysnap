using System.Text;
using System.Text.Json.Nodes;

namespace Tinysnap.Core.Tests;

/// <summary>Steps count from any start, in numbers or letters: a second capture of the same steps
/// can carry on from 4, and lettered callouts count apart from numbered steps.</summary>
public class StepOptionsTests
{
    private static Document Steps(bool[] letters, int start = 1) =>
        new(Fixture.Capture(200, 60),
            annotations: [.. letters.Select((letter, index) =>
                Fixture.Annotation(new AnnotationKind.Step(new Point(20 + index * 30, 20)), new Style(Palette.Red, letters: letter)))],
            stepStart: start);

    private static string[] Labels(Document document) => [.. document.Annotations.Select(a => document.StepLabel(a.Id) ?? "")];

    [Fact]
    public void StepsCountFromTheChosenStart() =>
        Assert.Equal(["4", "5", "6"], Labels(Steps([false, false, false], start: 4)));

    [Fact]
    public void LetterStepsCountApartFromNumberSteps()
    {
        Assert.Equal(["1", "A", "2", "B"], Labels(Steps([false, true, false, true])));
        Assert.Equal(["C", "D"], Labels(Steps([true, true], start: 3)));
    }

    [Fact]
    public void LettersRunOnPastZ() => Assert.Equal(["Y", "Z", "AA"], Labels(Steps([true, true, true], start: 25)));

    /// <summary>A wide label, AAA, at the size of 1 ran past the disc: white letters spilt onto the capture.</summary>
    [Fact]
    public void ALongLabelStaysInsideItsDisc()
    {
        var step = Fixture.Annotation(new AnnotationKind.Step(new Point(50, 50)), new Style(Palette.Red, StyleSize.ExtraSmall, letters: true));
        using var image = Renderer.Render(new Document(Fixture.Capture(100, 100, fill: Fixture.Blue), annotations: [step], stepStart: 703))!;
        var radius = step.Bounds(1).Width / 2;
        var outside = Enumerable.Range(0, 100).SelectMany(x => Enumerable.Range(0, 100).Select(y => (x, y)))
            .Where(p => Math.Sqrt(Math.Pow(p.x + 0.5 - 50, 2) + Math.Pow(p.y + 0.5 - 50, 2)) > radius + 1);
        Assert.All(outside, p => Assert.True(Fixture.Pixel(image, p.x, p.y).R < 60));
    }

    [Fact]
    public void TheLayersPanelNamesAStepByItsLabel()
    {
        var lettered = Steps([true]);
        Assert.Equal("Step A", lettered.LayerName(lettered.Annotations[0].Id));
    }

    [Fact]
    public void TheStartIsSavedWithTheCaptureAndAFileWithoutOneStartsAtOne()
    {
        var captured = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var (json, _) = DocumentArchive.Encode(Steps([false], start: 7), captured);
        Assert.Equal(7, DocumentArchive.Decode(json, _ => null).StepStart);
        var (plain, _) = DocumentArchive.Encode(Steps([false]), captured);
        Assert.DoesNotContain("stepStart", Encoding.UTF8.GetString(plain));
        Assert.Equal(1, DocumentArchive.Decode(plain, _ => null).StepStart);
    }

    [Fact]
    public void SettingTheStartIsOneUndoableStepWithinLimits()
    {
        var session = new EditorSession(Steps([false]));
        session.SetStepStart(5);
        Assert.Equal(5, session.Display.StepStart);
        session.Undo();
        Assert.Equal(1, session.Display.StepStart);
        session.SetStepStart(0);
        Assert.Equal(1, session.Display.StepStart);
        session.SetStepStart(5000);
        Assert.Equal(Document.StepStartMax, session.Display.StepStart);
    }

    [Fact]
    public void LettersAreSavedAndAStyleFromBeforeCountsInNumbers()
    {
        var style = new Style(Palette.Red, letters: true);
        Assert.Equal(style, Style.FromJson(JsonNode.Parse(style.ToJson().ToJsonString())));
        Assert.False(Style.FromJson(JsonNode.Parse("""{"colorHex":"#FF3B30","size":"medium"}""")).Letters);
        Assert.True(Tool.Step.HasCounter());
        Assert.False(Tool.Arrow.HasCounter());
    }
}
