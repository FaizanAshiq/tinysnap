using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Tinysnap.Core;

/// <summary>A word the text reader found, with its box in capture pixels, y growing downward.</summary>
public sealed record TextWord(string Text, Rect Box);

/// <summary>One line of words, left to right.</summary>
public sealed record TextLine(ImmutableArray<TextWord> Words)
{
    public bool Equals(TextLine? other) => other is not null && Words.SequenceEqual(other.Words);
    public override int GetHashCode() => Words.Aggregate(0, (hash, word) => HashCode.Combine(hash, word));
}

/// <summary>What Redact covers.</summary>
public enum RedactTarget { Emails, Phones, Numbers, AllText }

/// <summary>Finds what to redact among the words read from a capture. A match can run over several
/// words, as a phone number written +1 (555) 014-2297 does, and is covered whole.</summary>
public static partial class TextRedaction
{
    public static IReadOnlyList<Rect> Boxes(IEnumerable<TextLine> lines, RedactTarget target) =>
        [.. lines.SelectMany(line => LineBoxes(line, target))];

    private static IEnumerable<Rect> LineBoxes(TextLine line, RedactTarget target)
    {
        if (line.Words.IsEmpty) return [];
        var all = line.Words.Skip(1).Aggregate(line.Words[0].Box, (box, word) => box.Union(word.Box));
        if (target == RedactTarget.AllText) return [all];
        // The line as one string, words a space apart, and where each word sits in it.
        var text = string.Join(' ', line.Words.Select(word => word.Text));
        var starts = new List<int>();
        var at = 0;
        foreach (var word in line.Words)
        {
            starts.Add(at);
            at += word.Text.Length + 1;
        }
        return Matches(text, target).Select(match =>
        {
            var covered = line.Words.Select((word, index) => (word, index))
                .Where(pair => starts[pair.index] < match.End && starts[pair.index] + pair.word.Text.Length > match.Start)
                .Select(pair => pair.word.Box).ToList();
            return covered.Skip(1).Aggregate(covered[0], (box, next) => box.Union(next));
        });
    }

    /// <summary>What in <paramref name="text"/> is the target. Phone numbers take 7 to 15 digits, the
    /// most a phone number has, so a 16 digit card number is not one; numbers take four or more, so a
    /// lone 5 in "after 5 pm" stays.</summary>
    private static IEnumerable<(int Start, int End)> Matches(string text, RedactTarget target)
    {
        var (pattern, least, most) = target switch
        {
            RedactTarget.Emails => (Email(), 0, int.MaxValue),
            RedactTarget.Phones => (Phone(), 7, 15),
            _ => (Number(), 4, int.MaxValue),
        };
        return pattern.Matches(text)
            .Where(match => match.Value.Count(char.IsDigit) is var digits && digits >= least && digits <= most)
            .Select(match => (match.Index, match.Index + match.Length));
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"[+(]?\d[\d ().-]{5,}\d")]
    private static partial Regex Phone();

    [GeneratedRegex(@"[+(]?\d(?:[\d ().,/-]*\d)?")]
    private static partial Regex Number();
}
