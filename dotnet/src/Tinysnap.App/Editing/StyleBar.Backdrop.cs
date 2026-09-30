using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Tinysnap.Core;

namespace Tinysnap.App.Editing;

/// <summary>The Backdrop panel: the fill first, No backdrop among them, then padding, corners and
/// shadow once there is a backdrop. Turning one on brings the last one used back.</summary>
internal sealed partial class StyleBar
{
    private static readonly string[] FillNames =
        ["No backdrop", "Gradient from the capture", "Solid colour", "Desktop wallpaper", "Clear, see-through"];

    private static readonly string[] PaddingNames = ["Small padding", "Medium padding", "Large padding"];
    private static readonly string[] BackdropCornerNames = ["Square capture corners", "Round capture corners", "Rounder capture corners"];
    private static readonly double[] BackdropCornerRadii = [0, 2.5, 4.5];
    private static readonly string[] ShadowNames = ["No shadow", "Soft shadow", "Strong shadow"];

    private static readonly BackdropFill?[] Fills =
        [null, BackdropFill.Gradient, BackdropFill.Solid, BackdropFill.Wallpaper, BackdropFill.Clear];

    private void RefreshBackdrop()
    {
        IsVisible = true;
        var backdrop = canvas.Session.Display.Backdrop;
        if (backdropShown && lastBackdrop == backdrop) return;
        // Only the colour changed: redrawn where it stands, so the palette stays open.
        if (backdropShown && lastBackdrop is { } before && backdrop is { } now && before with { ColorHex = now.ColorHex } == now)
        {
            lastBackdrop = now;
            Recolor(now.ColorHex);
            return;
        }
        backdropShown = true;
        lastBackdrop = backdrop;
        Clear();
        BackdropFillChips = Group([.. Fills.Select((fill, index) => Chip(FillGlyph(fill), FillNames[index],
            fill is null ? backdrop is null : backdrop?.Fill == fill, () => PickFill(fill)))]);
        if (backdrop is null) return;
        if (backdrop.Fill == BackdropFill.Solid) row.Children.Add(ColorButton = MakeColorButton(backdrop.ColorHex));
        if (backdrop.Fill == BackdropFill.Wallpaper && backdrop.Wallpaper is null)
        {
            WallpaperNote = new TextBlock
            {
                Text = "Using the gradient",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush"),
            };
            ToolTip.SetTip(WallpaperNote, "The desktop picture could not be read, so the gradient stands in");
            row.Children.Add(WallpaperNote);
        }
        PaddingChips = Group([.. Enum.GetValues<BackdropPadding>().Select((padding, index) =>
            Chip(Glyphs.Icon(PaddingGlyph(index), 16), PaddingNames[index],
                 backdrop.Padding == padding, () => ChangeBackdrop(b => b with { Padding = padding })))]);
        BackdropCornerChips = Group([.. Backdrop.CornerChoices.Select((corners, index) =>
            Chip(Glyphs.Box(oval: false, filled: false, BackdropCornerRadii[index]),
                 BackdropCornerNames[index],
                 backdrop.Corners == corners, () => ChangeBackdrop(b => b with { Corners = corners })))]);
        ShadowChips = Group([.. Enum.GetValues<BackdropShadow>().Select((shadow, index) =>
            Chip(ShadowGlyph(index), ShadowNames[index],
                 backdrop.Shadow == shadow, () => ChangeBackdrop(b => b with { Shadow = shadow })))]);
    }

    private void PickFill(BackdropFill? fill)
    {
        if (fill is not { } picked)
        {
            canvas.SetBackdrop(null);
            return;
        }
        ChangeBackdrop(backdrop =>
        {
            var next = backdrop with { Fill = picked };
            return picked == BackdropFill.Wallpaper && next.Wallpaper is null ? next with { Wallpaper = ReadWallpaper?.Invoke() } : next;
        });
    }

    /// <summary>From the capture's backdrop, or the last one used when it has none.
    /// <paramref name="merging"/> is for the spectrum's stream of colours, remembered once the
    /// palette closes.</summary>
    private void ChangeBackdrop(Func<Backdrop, Backdrop> change, bool merging = false)
    {
        var next = change(canvas.Session.Display.Backdrop ?? Remembered());
        canvas.SetBackdrop(next, merging);
        if (!merging) Remember?.Invoke(next);
    }

    private static Control FillGlyph(BackdropFill? fill) => fill switch
    {
        null => Glyphs.Icon("F1 M5 5H15V15H5ZM6 6V14H14V6ZM5.6 14.4L14.4 5.6L15 6.2L6.2 15Z", 16),
        BackdropFill.Gradient => new Border
        {
            Width = 11,
            Height = 11,
            CornerRadius = new CornerRadius(2),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(138, 138, 138), 0), new GradientStop(Color.FromArgb(40, 138, 138, 138), 1) },
            },
        },
        BackdropFill.Solid => Glyphs.Icon("M5 5H15V15H5Z", 16),
        BackdropFill.Wallpaper => Glyphs.Icon("F1 M5 5H15V15H5ZM6 6V14H14V6ZM6.5 14L10 8.5L13.5 14Z", 16),
        _ => Glyphs.Icon("M5 5H8.33V8.33H5ZM11.67 5H15V8.33H11.67ZM8.33 8.33H11.67V11.67H8.33ZM5 11.67H8.33V15H5ZM11.67 11.67H15V15H11.67Z", 16),
    };

    /// <summary>A frame round a smaller and smaller middle.</summary>
    private static string PaddingGlyph(int index)
    {
        var inset = 1.5 + index * 1.5;
        var (a, b) = (4 + inset, 16 - inset);
        return FormattableString.Invariant($"F1 M3 3H17V17H3ZM4 4V16H16V4ZM{a} {a}H{b}V{b}H{a}Z");
    }

    /// <summary>A square with no shadow, a faint one, and a dark one.</summary>
    private static Control ShadowGlyph(int index)
    {
        var panel = new Panel { Width = 16, Height = 16 };
        if (index > 0)
            panel.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                Margin = new Thickness(5, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(2),
                Opacity = index == 1 ? 0.3 : 0.6,
                [!Border.BackgroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseHighBrush"),
            });
        panel.Children.Add(new Border
        {
            Width = 9,
            Height = 9,
            Margin = new Thickness(3, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1.4),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("SystemControlForegroundBaseHighBrush"),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush"),
        });
        return panel;
    }
}
