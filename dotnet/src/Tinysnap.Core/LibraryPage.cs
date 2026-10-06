namespace Tinysnap.Core;

/// <summary>One page of the library, newest first: fifty captures, so the library opens as quickly
/// with a month of captures in it as with a day's.</summary>
public sealed class LibraryPage
{
    public const int Size = 50;

    public IReadOnlyList<LibraryEntry> Entries { get; }

    /// <summary>From 0.</summary>
    public int Number { get; }

    public int Count { get; }

    /// <summary>Page <paramref name="number"/> of <paramref name="all"/>, or the last page when there
    /// are fewer: trashing the last capture on a page, or the sweep, shows the page before instead of
    /// an empty one.</summary>
    public LibraryPage(IReadOnlyList<LibraryEntry> all, int number)
    {
        Count = Math.Max(1, (all.Count + Size - 1) / Size);
        Number = Math.Clamp(number, 0, Count - 1);
        Entries = all.Skip(Number * Size).Take(Size).ToList();
    }

    public bool HasPrevious => Number > 0;

    public bool HasNext => Number < Count - 1;

    public string Title => $"Page {Number + 1} of {Count}";
}
