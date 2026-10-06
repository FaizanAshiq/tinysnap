using Tinysnap.Core;

namespace Tinysnap.Core.Tests;

public class LibraryPageTests
{
    /// <summary>Newest first, as the store lists them.</summary>
    private static List<LibraryEntry> Entries(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new LibraryEntry($"/library/{i}", DateTimeOffset.UnixEpoch.AddSeconds(-i)))];

    [Fact]
    public void FiftyCapturesAPageNewestFirst()
    {
        var all = Entries(120);
        var first = new LibraryPage(all, 0);
        Assert.Equal(all[..50], first.Entries);
        Assert.Equal(3, first.Count);
        Assert.Equal("Page 1 of 3", first.Title);
        Assert.False(first.HasPrevious);
        Assert.True(first.HasNext);
        var last = new LibraryPage(all, 2);
        Assert.Equal(all[100..], last.Entries);
        Assert.True(last.HasPrevious);
        Assert.False(last.HasNext);
    }

    [Fact]
    public void FiftyExactlyIsOnePage()
    {
        Assert.Equal(1, new LibraryPage(Entries(50), 0).Count);
        Assert.Equal(2, new LibraryPage(Entries(51), 0).Count);
    }

    /// <summary>Trashing the only capture on the last page shows the page before, not an empty one.</summary>
    [Fact]
    public void APageThatNoLongerExistsShowsTheLastOne()
    {
        var page = new LibraryPage(Entries(50), 1);
        Assert.Equal(0, page.Number);
        Assert.Equal(50, page.Entries.Count);
    }

    [Fact]
    public void AnEmptyLibraryIsOneEmptyPage()
    {
        var page = new LibraryPage([], 0);
        Assert.Equal(1, page.Count);
        Assert.Empty(page.Entries);
        Assert.False(page.HasPrevious);
        Assert.False(page.HasNext);
    }
}
