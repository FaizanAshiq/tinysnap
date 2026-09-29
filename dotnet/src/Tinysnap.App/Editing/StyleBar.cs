using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Tinysnap.Core;
using Style = Tinysnap.Core.Style;

namespace Tinysnap.App.Editing;

/// <summary>Floats over the canvas's top right corner and sets the style of the selection, or of
/// the next shape when nothing is selected: colour, size, outline or fill, corners, and a pasted
/// image's opacity and difference blend. Each chip changes only its own part.</summary>
internal sealed class StyleBar : Border
{
    private static readonly double[] Opacities = [0.25, 0.5, 0.75, 1];

    private static readonly string[] CornerNames =
        ["Square corners", "Slightly rounded", "Rounded", "Very rounded", "Fully round"];

    private readonly CanvasControl canvas;
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 14 };
    private (Tool Tool, Style Style, bool Selected)? shown;

    public Button? ColorButton { get; private set; }
    public IReadOnlyList<Button> Swatches { get; private set; } = [];
    public IReadOnlyList<ToggleButton> SizeChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> FillChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> CornerChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> OpacityChips { get; private set; } = [];
    public ToggleButton? DifferenceChip { get; private set; }
    public Button? DeleteChip { get; private set; }

    /// <summary>The colour spectrum for anything the swatches do not have.</summary>
    public ColorView? CustomColor { get; private set; }

    private Border? colorSwatch;

    /// <summary>Set while the spectrum is moved to match a colour picked elsewhere, so that move
    /// is not taken for a pick.</summary>
    private bool syncing;

    public StyleBar(CanvasControl canvas)
    {
        this.canvas = canvas;
        Child = row;
        Padding = new Thickness(12, 7);
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(0.5);
        this[!BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush");
        this[!BorderBrushProperty] = new DynamicResourceExtension("SystemControlForegroundBaseLowBrush");
        BoxShadow = BoxShadows.Parse("0 2 10 0 #40000000");
        canvas.Changed += Refresh;
        Refresh();
    }

    /// <summary>Whether a tool has anything to set, and so whether the bar shows at all.</summary>
    public static bool Shows(Tool tool) =>
        tool.HasColor() || tool.HasSize() || tool.HasFill() || tool.HasCorners() || tool.HasOverlay();

    public void Refresh()
    {
        var session = canvas.Session;
        var selected = session.SelectedAnnotation;
        var tool = selected?.Tool ?? session.Tool;
        var style = selected?.Style ?? session.StyleFor(tool);
        IsVisible = Shows(tool);
        var now = (tool, style, selected is not null);
        if (shown == now) return;
        // Only the colour changed: redrawn where it stands. Rebuilding replaced the button the
        // palette hangs from, which closed the palette in the middle of a drag on the spectrum.
        if (shown is { } before && before.Tool == tool && before.Selected == now.Item3
            && before.Style with { ColorHex = style.ColorHex } == style)
        {
            shown = now;
            Recolor(style.ColorHex);
            return;
        }
        shown = now;
        Rebuild(tool, style, selected is not null);
    }

    private void Rebuild(Tool tool, Style style, bool selected)
    {
        row.Children.Clear();
        ColorButton = null;
        Swatches = [];
        CustomColor = null;
        colorSwatch = null;
        DifferenceChip = null;
        DeleteChip = null;
        if (tool.HasColor()) row.Children.Add(ColorButton = MakeColorButton(style.ColorHex));
        SizeChips = tool.HasSize() ? Group(MakeSizeChips(tool, style)) : [];
        FillChips = tool.HasFill() ? Group(MakeFillChips(tool, style)) : [];
        CornerChips = tool.HasCorners() ? Group(MakeCornerChips(style)) : [];
        OpacityChips = tool.HasOverlay() ? Group(MakeOpacityChips(style)) : [];
        if (tool.HasOverlay())
        {
            var difference = Chip(Glyphs.Difference(), "Difference: what matches goes black", style.Difference,
                                  () => canvas.Restyle(s => s with { Difference = !s.Difference }));
            Group([difference]);
            DifferenceChip = difference;
        }
        if (selected)
        {
            var delete = new Button
            {
                Content = Glyphs.Icon(ToolIcons.Delete, 16),
                Padding = new Thickness(6, 4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            ToolTip.SetTip(delete, "Delete, or the Delete key");
            AutomationProperties.SetName(delete, "Delete");
            delete.Click += (_, _) => canvas.DeleteSelection();
            row.Children.Add(delete);
            DeleteChip = delete;
        }
    }

    private IReadOnlyList<ToggleButton> Group(IReadOnlyList<ToggleButton> chips)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var chip in chips) group.Children.Add(chip);
        row.Children.Add(group);
        return chips;
    }

    private ToggleButton Chip(Control glyph, string tip, bool chosen, Action pick)
    {
        var chip = new ToggleButton
        {
            Content = glyph,
            IsChecked = chosen,
            Width = 30,
            Height = 26,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(chip, tip);
        AutomationProperties.SetName(chip, tip);
        chip.Click += (_, _) => pick();
        return chip;
    }

    // Colour

    private Button MakeColorButton(string hex)
    {
        var swatches = Palette.Swatches.Select(swatch =>
        {
            var button = new Button
            {
                Tag = swatch,
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.Parse(swatch)),
                BorderThickness = new Thickness(swatch == hex ? 2.5 : 0.5),
            };
            button[!BorderBrushProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush");
            ToolTip.SetTip(button, swatch);
            AutomationProperties.SetName(button, swatch);
            button.Click += (_, _) => ChooseColor(swatch);
            return button;
        }).ToArray();
        Swatches = swatches;

        var field = new TextBox { PlaceholderText = "#RRGGBB", Width = 116 };
        field.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (!TryHex(field.Text ?? "")) field.SelectAll();
        };
        var grid = new UniformGrid { Columns = 4 };
        foreach (var swatch in swatches) grid.Children.Add(swatch);
        CustomColor = new ColorView
        {
            Color = Color.Parse(hex),
            IsColorPaletteVisible = false,
            IsColorComponentsVisible = false,
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            IsAccentColorsVisible = false,
            Width = 240,
        };
        // A drag on the spectrum sends a stream of colours, which undoes as one step.
        CustomColor.ColorChanged += (_, e) =>
        {
            if (syncing) return;
            var picked = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
            canvas.Restyle(style => style with { ColorHex = picked }, merging: true);
        };
        var palette = new StackPanel { Spacing = 10, Margin = new Thickness(4), Children = { grid, field, CustomColor } };

        var button = new Button
        {
            Content = colorSwatch = new Border
            {
                Width = 22,
                Height = 16,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderThickness = new Thickness(1),
                [!BorderBrushProperty] = new DynamicResourceExtension("SystemControlForegroundBaseLowBrush"),
            },
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Flyout = new Flyout { Content = palette },
        };
        ToolTip.SetTip(button, $"Colour {hex}");
        AutomationProperties.SetName(button, $"Colour {hex}");
        return button;
    }

    /// <summary>The colour button, swatch outlines and spectrum brought to <paramref name="hex"/>.</summary>
    private void Recolor(string hex)
    {
        var color = Color.Parse(hex);
        if (colorSwatch is not null) colorSwatch.Background = new SolidColorBrush(color);
        if (ColorButton is not null)
        {
            ToolTip.SetTip(ColorButton, $"Colour {hex}");
            AutomationProperties.SetName(ColorButton, $"Colour {hex}");
        }
        foreach (var swatch in Swatches) swatch.BorderThickness = new Thickness((string?)swatch.Tag == hex ? 2.5 : 0.5);
        if (CustomColor is null || CustomColor.Color == color) return;
        syncing = true;
        CustomColor.Color = color;
        syncing = false;
    }

    private void ChooseColor(string hex)
    {
        ColorButton?.Flyout?.Hide();
        canvas.Restyle(style => style with { ColorHex = hex });
    }

    /// <summary>A custom colour typed as "#RRGGBB", with or without the #. False, and nothing
    /// changed, for anything else.</summary>
    public bool TryHex(string text)
    {
        var digits = text.Trim().TrimStart('#').ToUpperInvariant();
        var hex = "#" + digits;
        if (Palette.Components(hex) is null) return false;
        ChooseColor(hex);
        return true;
    }

    // Size, fill, corners and overlay

    private List<ToggleButton> MakeSizeChips(Tool tool, Style style) =>
        [.. Enum.GetValues<StyleSize>().Select((size, index) =>
        {
            Control glyph = tool switch
            {
                Tool.Text => Glyphs.Letter(9 + index * 2.5),
                Tool.Arrow or Tool.Line or Tool.Rectangle or Tool.Oval or Tool.Freehand or Tool.Highlighter or Tool.Measure
                    => Glyphs.Line(1 + index * 1.3),
                _ => Glyphs.Dot(4 + index * 2.2),
            };
            return Chip(glyph, $"Size {index + 1} of 5, [ and ] to step", style.Size == size,
                        () => canvas.Restyle(s => s with { Size = size }));
        })];

    private List<ToggleButton> MakeFillChips(Tool tool, Style style) =>
        [.. new[] { false, true }.Select(filled =>
            Chip(Glyphs.Box(oval: tool == Tool.Oval, filled), filled ? "Filled" : "Outline", style.Filled == filled,
                 () => canvas.Restyle(s => s with { Filled = filled })))];

    private List<ToggleButton> MakeCornerChips(Style style) =>
        [.. Enum.GetValues<CornerSize>().Select((corners, index) =>
            Chip(Glyphs.Box(oval: false, filled: false, radius: corners == CornerSize.Full ? 6 : new[] { 0, 1.5, 3, 5 }[index]),
                 CornerNames[index], style.Corners == corners, () => canvas.Restyle(s => s with { Corners = corners })))];

    private List<ToggleButton> MakeOpacityChips(Style style) =>
        [.. Opacities.Select(opacity =>
            Chip(Glyphs.Faded(opacity), $"Opacity {(int)(opacity * 100)}%, or keys 1 to 9 and 0",
                 Math.Abs(style.Opacity - opacity) < 0.01, () => canvas.Restyle(s => s with { Opacity = opacity })))];
}
