namespace Tinysnap.Core.Tests;

public class LibraryStoreTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    /// <summary>2026-09-25 02:42:10 UTC.</summary>
    private static readonly DateTimeOffset Captured = DateTimeOffset.FromUnixTimeSeconds(1_790_304_130);

    [Fact]
    public void TheLibraryLivesWithDataOnLinuxAndBesideThePreferencesElsewhere()
    {
        var expected = Environment.GetFolderPath(OperatingSystem.IsLinux() ? Environment.SpecialFolder.LocalApplicationData
                                                                           : Environment.SpecialFolder.ApplicationData);
        Assert.Equal(Path.Combine(expected, "Tinysnap", "Library"), LibraryStore.DefaultRoot);
    }

    private static LibraryStore Store() => new(Path.Combine(Path.GetTempPath(), $"tinysnap-library-{Guid.NewGuid()}"));

    private static OpenedEntry Opened(LibraryStore library, LibraryEntry entry)
    {
        var opened = library.Open(entry);
        Assert.NotNull(opened);
        return opened;
    }

    [Fact]
    public void AWallpaperIsKeptBesideTheEditsAndGoesWhenTheBackdropDoes()
    {
        var library = Store();
        var entry = library.Add(Fixture.Capture(40, 30, 2), Captured, Utc);
        var document = Opened(library, entry).Document;
        var wallpaper = new BackdropWallpaper(Guid.NewGuid(), new PastedImage(Fixture.CaptureImage(8, 4)));
        var backdrop = new Backdrop(BackdropFill.Wallpaper, "#007AFF", BackdropPadding.Small, CornerSize.Medium,
                                    BackdropShadow.Soft, wallpaper);
        document = document with { Backdrop = backdrop };
        library.SaveEdits(document, entry);
        var file = Path.Combine(entry.Folder, $"backdrop-{Json.Uuid(wallpaper.Id)}.png");
        Assert.True(File.Exists(file));
        Assert.Equal(backdrop, Opened(library, entry).Document.Backdrop);

        document = document with { Backdrop = null };
        library.SaveEdits(document, entry);
        Assert.False(File.Exists(file));
        Assert.Null(Opened(library, entry).Document.Backdrop);
    }

    [Fact]
    public void AnEntryReopensWithItsSizeAndItsImageIsDrawnAtIt()
    {
        var library = Store();
        var entry = library.Add(Fixture.Capture(40, 30, 2), Captured, Utc);
        var document = Opened(library, entry).Document with { Resize = 0.5 };
        library.SaveEdits(document, entry);
        library.SaveImage(document, entry);
        Assert.Equal(0.5, Opened(library, entry).Document.Resize);
        Assert.Equal(20, LibraryStore.ReadImage(entry.ImagePath)?.Image.Width);
    }

    [Fact]
    public void ASavedSizePastTheLongestSideIsHeldToItOnOpening()
    {
        // Only the Size panel held a size to 16,384 pixels, so an edits.json asking for 400%
        // of a wide capture would have rendered something far past it.
        var library = Store();
        var entry = library.Add(Fixture.Capture(5000, 10), Captured, Utc);
        library.SaveEdits(Opened(library, entry).Document with { Resize = 4 }, entry);
        var opened = Opened(library, entry).Document;
        Assert.True(opened.Resize == opened.LargestResize() && opened.LargestResize() < 4);
    }

    [Fact]
    public void AnImageDrawnFromOlderEditsStillReadsAsStale()
    {
        // A render off the UI thread can land after newer edits were written. Stamped with
        // the edits it was drawn from, it still reads as stale and is drawn again.
        var library = Store();
        var entry = library.Add(Fixture.Capture(40, 30, 2), Captured, Utc);
        var older = library.EditsDate(entry);
        Assert.NotNull(older);
        var document = Opened(library, entry).Document with
        {
            Annotations = [Fixture.Annotation(new AnnotationKind.Rectangle(new Rect(2, 2, 10, 10)))],
        };
        library.SaveEdits(document, entry);
        File.SetLastWriteTimeUtc(entry.EditsPath, older.Value.AddSeconds(5).UtcDateTime);
        library.SaveImage(document, entry, older);
        Assert.True(library.ImageIsStale(entry));
        library.SaveImage(document, entry, library.EditsDate(entry));
        Assert.False(library.ImageIsStale(entry));
    }

    [Fact]
    public void AnImageStampedWithItsEditsIsNotStaleWhateverTheirNanoseconds()
    {
        // A file date goes through a date value and back when an image is stamped, and on
        // the Mac came back a few hundred nanoseconds early. Read as stale, it was drawn again
        // for as long as the library stayed open. A .NET tick is 100 nanoseconds.
        var library = Store();
        var entry = library.Add(Fixture.Capture(40, 30, 2), Captured, Utc);
        var document = Opened(library, entry).Document;
        foreach (var nanoseconds in new long[] { 100, 123_456_700, 502_000_100, 999_999_900 })
        {
            File.SetLastWriteTimeUtc(entry.EditsPath, Captured.UtcDateTime.AddTicks(nanoseconds / 100));
            library.SaveImage(document, entry, library.EditsDate(entry));
            Assert.False(library.ImageIsStale(entry), $"edits at {nanoseconds} ns");
        }
    }

    [Fact]
    public void ACaptureIsKeptAsThreeFilesInAFolderNamedForItsTime()
    {
        var library = Store();
        var entry = library.Add(Fixture.Capture(40, 30, 2), Captured.AddSeconds(0.6), Utc);

        Assert.Equal("2026-09-25 02.42.10", entry.Name);
        Assert.Equal(Captured, entry.Captured);
        Assert.True(File.Exists(entry.OriginalPath) && File.Exists(entry.EditsPath) && File.Exists(entry.ImagePath));
        var opened = Opened(library, entry);
        Assert.True(opened.IsEditable);
        Assert.Equal(2, opened.Document.Scale);
        Assert.Equal(new Size(40, 30), opened.Document.Capture.PixelSize);
        Assert.Empty(opened.Document.Annotations);
    }

    [Fact]
    public void CapturesInTheSameSecondGetANumberAppended()
    {
        var library = Store();
        var names = Enumerable.Range(0, 3).Select(_ => library.Add(Fixture.Capture(4, 4), Captured, Utc).Name).ToArray();
        Assert.Equal(["2026-09-25 02.42.10", "2026-09-25 02.42.10 2", "2026-09-25 02.42.10 3"], names);
    }

    [Fact]
    public void EditsComeBackEditableAndUnusedPastedImagesGo()
    {
        var library = Store();
        var entry = library.Add(Fixture.Capture(100, 80, 2), Captured, Utc);
        var pasted = new PastedImage(Fixture.CaptureImage(6, 5, Fixture.Blue));
        var arrow = Fixture.Annotation(new AnnotationKind.Arrow(new Point(5, 5), new Point(60, 40)));
        var image = Fixture.Annotation(new AnnotationKind.Image(new Rect(10, 10, 12, 10), pasted));
        var document = Opened(library, entry).Document with { Crop = new Rect(2, 2, 90, 70), Annotations = [arrow, image] };

        library.SaveEdits(document, entry);
        var pastedPath = Path.Combine(entry.Folder, $"pasted-{Json.Uuid(image.Id)}.png");
        Assert.True(File.Exists(pastedPath));
        var reopened = Opened(library, entry).Document;
        Assert.Equal(document.Crop, reopened.Crop);
        Assert.Equal(arrow, reopened.Annotations[0]);
        var kind = Assert.IsType<AnnotationKind.Image>(reopened.Annotations[^1].Kind);
        Assert.Equal(new Rect(10, 10, 12, 10), kind.Rect);
        Assert.Equal(6, kind.Pasted.Image.Width);

        library.SaveEdits(document with { Annotations = [arrow] }, entry);
        Assert.False(File.Exists(pastedPath));
    }

    /// <summary>A file time only moves with the clock's tick, 15 ms on Windows, so edits written
    /// just after the image can carry its time or an earlier one. They still make it stale.</summary>
    [Fact]
    public void EditsWrittenAfterTheImageMakeItStaleWhateverTheClockSays()
    {
        var library = Store();
        var entry = library.Add(Fixture.Capture(100, 80, 2), Captured, Utc);
        var document = Opened(library, entry).Document;
        library.SaveImage(document, entry, editsAsOf: DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(library.ImageIsStale(entry));
        library.SaveEdits(document with { Crop = new Rect(0, 0, 50, 40) }, entry);
        Assert.True(library.ImageIsStale(entry));
    }

    [Fact]
    public void TheImageIsTheRenderedCropAndGoesStaleWhenEditsAreNewer()
    {
        var library = Store();
        var entry = library.Add(Fixture.Capture(100, 80, 2), Captured, Utc);
        var document = Opened(library, entry).Document with
        {
            Crop = new Rect(10, 10, 50, 40),
            Annotations = [Fixture.Annotation(new AnnotationKind.Erase(new Rect(10, 10, 20, 20)))],
        };

        library.SaveEdits(document, entry);
        Assert.True(library.ImageIsStale(entry));
        library.SaveImage(document, entry);
        Assert.False(library.ImageIsStale(entry));
        var flat = LibraryStore.ReadImage(entry.ImagePath);
        Assert.NotNull(flat);
        Assert.True(flat.Value.Image.Width == 50 && flat.Value.Image.Height == 40);
        Assert.Equal(2, flat.Value.Scale);
    }

    [Fact]
    public void EntriesListNewestFirstAndLeaveOutFoldersWithNoImage()
    {
        var library = Store();
        foreach (var offset in new[] { 0, 120, 60 }) library.Add(Fixture.Capture(4, 4), Captured.AddSeconds(offset), Utc);
        Directory.CreateDirectory(Path.Combine(library.Root, "not a capture"));
        Assert.Equal(["2026-09-25 02.44.10", "2026-09-25 02.43.10", "2026-09-25 02.42.10"],
                     library.Entries().Select(entry => entry.Name));
    }

    [Fact]
    public void ADamagedEntryOpensFlatFromTheBestImageLeft()
    {
        var library = Store();
        var entry = library.Add(Fixture.Capture(30, 20, 2), Captured, Utc);
        library.SaveImage(Opened(library, entry).Document with { Crop = new Rect(0, 0, 10, 10) }, entry);
        File.WriteAllText(entry.EditsPath, "{ not json");

        var flat = Opened(library, entry);
        Assert.False(flat.IsEditable);
        Assert.Equal(new Size(10, 10), flat.Document.Capture.PixelSize);
        Assert.Equal(2, flat.Document.Scale);
        Assert.Equal([Captured], library.Entries().Select(e => e.Captured));

        File.Delete(entry.ImagePath);
        Assert.Equal(new Size(30, 20), library.Open(entry)?.Document.Capture.PixelSize);
        File.Delete(entry.OriginalPath);
        Assert.Null(library.Open(entry));
        Assert.Empty(library.Entries());
    }

    [Fact]
    public void TheSweepDeletesOnlyEntriesPastThirtyDaysAndSparesOpenOnes()
    {
        var library = Store();
        var now = Captured;
        var fresh = library.Add(Fixture.Capture(4, 4), now.AddDays(-29), Utc);
        var old = library.Add(Fixture.Capture(4, 4), now.AddDays(-31), Utc);
        var open = library.Add(Fixture.Capture(4, 4), now.AddDays(-40), Utc);

        var removed = library.Sweep(now, new HashSet<string> { open.Name });
        Assert.Equal([old.Name], removed.Select(entry => entry.Name));
        Assert.Equal(new HashSet<string> { fresh.Name, open.Name }, library.Entries().Select(entry => entry.Name).ToHashSet());
    }

    [Fact]
    public void ClearingSparesOpenEntriesAndTheSizeCountsEveryFile()
    {
        var library = Store();
        var kept = library.Add(Fixture.Capture(50, 50), Captured, Utc);
        library.Add(Fixture.Capture(50, 50), Captured.AddSeconds(1), Utc);
        var before = library.Size();
        Assert.True(before > 0);

        library.Clear(new HashSet<string> { kept.Name });
        Assert.Equal([kept.Name], library.Entries().Select(entry => entry.Name));
        Assert.True(library.Size() < before);
    }

    [Fact]
    public void AnEditsFileCannotReachAnImageOutsideItsEntry()
    {
        // A pasted image's file name is read from edits.json. Joined unchecked, a tampered name
        // read a picture from anywhere on disk into the document.
        var library = Store();
        var entry = library.Add(Fixture.Capture(40, 30, 2), Captured, Utc);
        var pasted = Fixture.Annotation(new AnnotationKind.Image(new Rect(0, 0, 4, 4), new PastedImage(Fixture.CaptureImage(4, 4))));
        library.SaveEdits(Opened(library, entry).Document with { Annotations = [pasted] }, entry);
        var name = $"pasted-{Json.Uuid(pasted.Id)}.png";
        var outside = Path.Combine(library.Root, "outside.png");
        File.Copy(Path.Combine(entry.Folder, name), outside);
        var json = File.ReadAllText(entry.EditsPath);
        foreach (var tampered in new[] { "../outside.png", outside })
        {
            File.WriteAllText(entry.EditsPath, json.Replace(name, tampered.Replace("\\", "\\\\")));
            Assert.False(Opened(library, entry).IsEditable, tampered);
        }
    }
}
