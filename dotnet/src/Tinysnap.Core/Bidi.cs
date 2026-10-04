using System.Globalization;

namespace Tinysnap.Core;

/// <summary>The part of the Unicode bidirectional algorithm a typed line needs, so a line mixing
/// Urdu or Arabic with English reads in the order Core Text gives it on the Mac: the line takes
/// the direction of its first strong letter, a number reads left to right and belongs to the text
/// before it, spaces and punctuation go with the letters on both sides of them, and runs are
/// turned round level by level.</summary>
// ponytail: no explicit embedding or isolate marks, and only the two number rules a typed note
// meets; a full UAX 9 implementation if pasted text ever carries them.
internal static class Bidi
{
    private enum Kind { Left, Right, Number, Neutral, Mark }

    /// <summary>The level of each UTF-16 unit of <paramref name="line"/>: even reads left to right,
    /// odd right to left. Both units of a surrogate pair share one.</summary>
    internal static int[] Levels(string line)
    {
        var kinds = new Kind[line.Length];
        for (var index = 0; index < line.Length; index++)
        {
            if (index > 0 && char.IsLowSurrogate(line[index]) && char.IsHighSurrogate(line[index - 1]))
            {
                kinds[index] = kinds[index - 1];
                continue;
            }
            var codepoint = char.IsSurrogatePair(line, index) ? char.ConvertToUtf32(line[index], line[index + 1]) : line[index];
            var kind = Classify(codepoint);
            // A mark, or a joiner, goes with the letter it sits on.
            kinds[index] = kind == Kind.Mark ? index > 0 ? kinds[index - 1] : Kind.Neutral : kind;
        }

        var first = Array.FindIndex(kinds, kind => kind is Kind.Left or Kind.Right);
        var rightToLeft = first >= 0 && kinds[first] == Kind.Right;
        // Each unit's direction once numbers are settled, true for right to left; null for a
        // space or punctuation until the letters around it decide.
        var directions = new bool?[line.Length];
        var lastStrong = rightToLeft;
        for (var index = 0; index < line.Length; index++)
        {
            switch (kinds[index])
            {
                case Kind.Left: directions[index] = lastStrong = false; break;
                case Kind.Right: directions[index] = lastStrong = true; break;
                // After a left to right letter a number is part of it; anywhere else it counts as
                // right to left for the spaces beside it, and reads left to right itself.
                case Kind.Number: directions[index] = lastStrong; break;
            }
        }

        var levels = new int[line.Length];
        for (var index = 0; index < line.Length;)
        {
            if (directions[index] is { } direction)
            {
                levels[index] = Level(kinds[index], direction, rightToLeft);
                index++;
                continue;
            }
            var end = index;
            while (end < line.Length && directions[end] is null) end++;
            var before = index > 0 ? directions[index - 1]!.Value : rightToLeft;
            var after = end < line.Length ? directions[end]!.Value : rightToLeft;
            var neutral = before == after ? before : rightToLeft;
            for (; index < end; index++) levels[index] = Level(Kind.Neutral, neutral, rightToLeft);
        }
        return levels;
    }

    private static int Level(Kind kind, bool direction, bool lineRightToLeft) =>
        kind == Kind.Number && direction ? 2
        : lineRightToLeft ? direction ? 1 : 2
        : direction ? 1 : 0;

    private static Kind Classify(int codepoint)
    {
        if (codepoint is >= '0' and <= '9' or >= 0x0660 and <= 0x0669 or >= 0x06F0 and <= 0x06F9) return Kind.Number;
        if (codepoint == 0x200E) return Kind.Left;
        if (codepoint == 0x200F) return Kind.Right;
        var category = CharUnicodeInfo.GetUnicodeCategory(codepoint);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            return Kind.Mark;
        // Hebrew, Arabic and the scripts beside them. Their punctuation reads right to left too,
        // except the Arabic comma, which goes with what is around it.
        if (codepoint is >= 0x0590 and <= 0x08FF or >= 0xFB1D and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF
                or >= 0x10800 and <= 0x10FFF or >= 0x1E800 and <= 0x1EFFF)
            return codepoint == 0x060C ? Kind.Neutral : Kind.Right;
        return category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.SpacingCombiningMark
            ? Kind.Left
            : Kind.Neutral;
    }

    /// <summary><paramref name="runs"/> in reading order, put in the order they are drawn left to
    /// right: from the highest level down to the lowest odd one, each stretch of runs at that
    /// level or above is turned round.</summary>
    internal static List<T> Reorder<T>(IReadOnlyList<T> runs, Func<T, int> level)
    {
        var order = runs.ToList();
        if (order.Count == 0) return order;
        var highest = order.Max(level);
        var lowestOdd = order.Select(level).Where(each => each % 2 == 1).DefaultIfEmpty(highest + 1).Min();
        for (var at = highest; at >= lowestOdd; at--)
        {
            for (var start = 0; start < order.Count;)
            {
                if (level(order[start]) < at)
                {
                    start++;
                    continue;
                }
                var end = start;
                while (end < order.Count && level(order[end]) >= at) end++;
                order.Reverse(start, end - start);
                start = end;
            }
        }
        return order;
    }
}
