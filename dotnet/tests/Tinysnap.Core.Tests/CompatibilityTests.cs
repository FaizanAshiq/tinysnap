namespace Tinysnap.Core.Tests;

/// <summary>The Mac app keeps the same library. Each app writes one entry holding every kind
/// of edit, and each app's tests open the other's. <c>TINYSNAP_WRITE_FIXTURES=1</c> writes this
/// app's entry again; otherwise the writer does nothing.</summary>
public class CompatibilityTests
{
    private static readonly DateTimeOffset Captured = DateTimeOffset.FromUnixTimeSeconds(1_790_304_130);

    /// <summary><c>dotnet/tests/fixtures</c>, found from the build output by the solution file.</summary>
    private static string Fixtures
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Tinysnap.slnx"))) folder = folder.Parent;
            Assert.NotNull(folder);
            return Path.Combine(folder.FullName, "tests", "fixtures");
        }
    }

    /// <summary>Fixed, so both apps build the same annotations.</summary>
    private static Guid Id(int number) => Guid.Parse($"00000000-0000-4000-8000-{number:D12}");

    /// <summary>Every annotation kind, styles off their defaults, a crop, a size, a measurement
    /// whose tag slid off its middle, a pasted image and a wallpaper backdrop. The Swift tests
    /// build the same document.</summary>
    private static Document Everything(double scale)
    {
        var red = new Style("#FF3B30");
        var pasted = new PastedImage(Fixture.CaptureImage(8, 6, Fixture.Blue));
        Annotation[] annotations =
        [
            new(Id(1), new AnnotationKind.Arrow(new Point(10, 20), new Point(110.5, 80)), red),
            new(Id(2), new AnnotationKind.Line(new Point(5, 5), new Point(50, 5)), new Style("#AF52DE", StyleSize.ExtraSmall)),
            new(Id(3), new AnnotationKind.Rectangle(new Rect(20, 30, 100, 60)),
                new Style("#007AFF", StyleSize.Large, filled: true, corners: CornerSize.Full)),
            new(Id(4), new AnnotationKind.Oval(new Rect(40, 40, 30, 20)), red),
            new(Id(5), new AnnotationKind.Text(new Point(12, 14), "Two\nlines, Größe اردو ✓"), new Style("#000000", StyleSize.ExtraLarge)),
            new(Id(6), new AnnotationKind.Highlighter(new Point(0, 90), new Point(90, 90)), new Style("#FFCC00")),
            new(Id(7), new AnnotationKind.Freehand([new Point(1, 2), new Point(3, 4), new Point(5, 7)]), red),
            new(Id(8), new AnnotationKind.Step(new Point(70, 70)), red),
            new(Id(9), new AnnotationKind.Spotlight(new Rect(10, 10, 40, 40)), new Style("#FF3B30", corners: CornerSize.Small)),
            new(Id(10), new AnnotationKind.Magnifier(new Point(150, 100), 30, 2.5), red),
            new(Id(11), new AnnotationKind.Image(new Rect(100, 10, 40, 30), pasted),
                new Style("#34C759", corners: CornerSize.Square, opacity: 0.5, difference: true)),
            new(Id(12), new AnnotationKind.Blur(new Rect(0, 0, 20, 20)), red),
            new(Id(13), new AnnotationKind.Pixelate(new Rect(20, 0, 20, 20)), red),
            new(Id(14), new AnnotationKind.Erase(new Rect(40, 0, 20, 20)), red),
            new(Id(15), new AnnotationKind.Measure(new Point(150, 20), new Point(150, 180)), red, 0.3),
        ];
        var wallpaper = new BackdropWallpaper(Id(99), new PastedImage(Fixture.CaptureImage(16, 10, Fixture.Green)));
        var backdrop = new Backdrop(BackdropFill.Wallpaper, "#34C759", BackdropPadding.Large, CornerSize.Large,
                                    BackdropShadow.Strong, wallpaper);
        return new Document(Fixture.Capture(300, 200, scale), new Rect(4, 6, 250, 150), [.. annotations], backdrop, 0.5);
    }

    [Fact]
    public void WritesTheDotnetFixtures()
    {
        if (Environment.GetEnvironmentVariable("TINYSNAP_WRITE_FIXTURES") is null) return;
        var library = new LibraryStore(Path.Combine(Path.GetTempPath(), $"tinysnap-fixtures-{Guid.NewGuid()}"));
        var document = Everything(1.5);
        var entry = library.Add(document.Capture, Captured, TimeZoneInfo.Utc);
        library.SaveEdits(document, entry);
        library.SaveImage(document, entry);
        var target = Path.Combine(Fixtures, "dotnet", "everything");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);
        // Copied rather than moved: the scratch folder may sit on another volume.
        foreach (var file in Directory.EnumerateFiles(entry.Folder)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    }

    [Fact]
    public void ReadsTheMacFixtures()
    {
        var folder = Path.Combine(Fixtures, "mac", "everything");
        var opened = new LibraryStore(Path.GetDirectoryName(folder)).Open(new LibraryEntry(folder, Captured));
        Assert.NotNull(opened);
        Assert.True(opened.IsEditable);

        var document = opened.Document;
        var expected = Everything(2);
        Assert.Equal(2, document.Scale);
        Assert.Equal(expected.Capture.PixelSize, document.Capture.PixelSize);
        Assert.Equal(expected.Crop, document.Crop);
        Assert.Equal(expected.Resize, document.Resize);
        Assert.Equal(expected.Backdrop, document.Backdrop);
        Assert.Equal(16, document.Backdrop?.Wallpaper?.Image.Image.Width);
        // A pasted image comes back as new pixels, so it is checked by size and swapped for the
        // original to compare everything else exactly.
        Annotation Restored(Annotation annotation)
        {
            if (annotation.Kind is not AnnotationKind.Image(var rect, var image)
                || expected.Annotation(annotation.Id)?.Kind is not AnnotationKind.Image(_, var original))
                return annotation;
            Assert.True(image.Image.Width == 8 && image.Image.Height == 6);
            return annotation with { Kind = new AnnotationKind.Image(rect, original) };
        }
        Assert.Equal(expected.Annotations, document.Annotations.Select(Restored));
    }
}
