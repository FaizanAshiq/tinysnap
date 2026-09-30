using Avalonia;
using Avalonia.Controls;

namespace Tinysnap.App.Library;

/// <summary>Tiles share each row: as many as sit nearest 230 wide, stretched to fill it, so the
/// gaps and the edges stay the same at any window width.</summary>
internal static class LibraryLayout
{
    public const double Gap = 16;
    public const double Edge = 24;
    private const double IdealTile = 230;

    public static (int Columns, double Tile) Columns(double width)
    {
        var usable = width - Edge * 2;
        var columns = Math.Max(1, (int)Math.Round((usable + Gap) / (IdealTile + Gap), MidpointRounding.AwayFromZero));
        return (columns, Math.Floor((usable - Gap * (columns - 1)) / columns));
    }

    /// <summary>An 8 point margin round a 16 by 10 picture, then the labels' row.</summary>
    public static double TileHeight(double width) => Math.Round(8 + (width - 16) * 10 / 16 + 32, MidpointRounding.AwayFromZero);
}

/// <summary>One day's tiles, laid out by <see cref="LibraryLayout"/>, with the edge margin on
/// both sides.</summary>
internal sealed class TileGrid : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1000 : availableSize.Width;
        var (columns, tile) = LibraryLayout.Columns(width);
        var height = LibraryLayout.TileHeight(tile);
        foreach (var child in Children) child.Measure(new Size(tile, height));
        var rows = (Children.Count + columns - 1) / columns;
        return new Size(width, rows == 0 ? 0 : rows * height + (rows - 1) * LibraryLayout.Gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, tile) = LibraryLayout.Columns(finalSize.Width);
        var height = LibraryLayout.TileHeight(tile);
        for (var index = 0; index < Children.Count; index++)
        {
            var (row, column) = Math.DivRem(index, columns);
            Children[index].Arrange(new Rect(LibraryLayout.Edge + column * (tile + LibraryLayout.Gap),
                                             row * (height + LibraryLayout.Gap), tile, height));
        }
        return finalSize;
    }

    /// <summary>Tiles in a row, for moving the selection up and down.</summary>
    public int ColumnCount => LibraryLayout.Columns(Bounds.Width > 0 ? Bounds.Width : 1000).Columns;
}
