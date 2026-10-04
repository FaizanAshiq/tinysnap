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

/// <summary>What the style bar shows: the tool's style, the capture's backdrop, or its export size.</summary>
internal enum StyleBarMode { Tool, Backdrop, Size }

/// <summary>Floats over the canvas's top right corner and sets the style of the selection, or of
/// the next shape when nothing is selected: colour, size, outline or fill, corners, and a pasted
/// image's opacity and difference blend. Each chip changes only its own part. The Backdrop and
/// Size panels show in its place until another tool or selection is picked.</summary>
internal sealed partial class StyleBar : Border
{
    private StyleBarMode mode;
    private (Tool Tool, Guid? Selection) panelOpenedOver;
    private bool backdropShown;
    private Backdrop? lastBackdrop;

    /// <summary>The backdrop a capture starts with when one is turned on: the last one used.</summary>
    public Func<Backdrop> Remembered { get; set; } = () => Backdrop.Defaults;

    /// <summary>The desktop picture, softened, for the wallpaper fill; null when it cannot be read.</summary>
    public Func<BackdropWallpaper?>? ReadWallpaper { get; set; }

    /// <summary>Told each finished backdrop change, so the next capture starts from it.</summary>
    public Action<Backdrop>? Remember { get; set; }

    public IReadOnlyList<ToggleButton> BackdropFillChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> PaddingChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> BackdropCornerChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> ShadowChips { get; private set; } = [];

    /// <summary>Says the gradient stands in for a desktop picture that could not be read.</summary>
    public TextBlock? WallpaperNote { get; private set; }

    public StyleBarMode Mode
    {
        get => mode;
        set
        {
            mode = value;
            panelOpenedOver = (canvas.Session.Tool, canvas.Session.Selection);
            shown = null;
            backdropShown = false;
            shownSize = null;
            Refresh();
        }
    }

    private static readonly double[] Opacities = [0.25, 0.5, 0.75, 1];

    private static readonly string[] CornerNames =
        ["Square corners", "Slightly rounded", "Rounded", "Very rounded", "Fully round"];

    private static readonly string[] AlignNames = ["Align left", "Centre", "Align right"];

    /// <summary>Three lines set left, centred and right, as the Mac's chips draw them.</summary>
    private static readonly string[] AlignGlyphs =
    [
        "M4 5H16V6.6H4ZM4 9.2H11V10.8H4ZM4 13.4H14V15H4Z",
        "M4 5H16V6.6H4ZM6.5 9.2H13.5V10.8H6.5ZM5 13.4H15V15H5Z",
        "M4 5H16V6.6H4ZM9 9.2H16V10.8H9ZM6 13.4H16V15H6Z",
    ];

    /// <summary>Plain text, or text on a box: two lines alone, or cut out of a box, as the Mac's
    /// chips draw them.</summary>
    private const string PlainTextGlyph = "M4 7.2H16V8.8H4ZM4 11.2H11.2V12.8H4Z";
    private const string TextOnABoxGlyph =
        "F0 M5.5 4H14.5A2.5 2.5 0 0 1 17 6.5V13.5A2.5 2.5 0 0 1 14.5 16H5.5A2.5 2.5 0 0 1 3 13.5V6.5A2.5 2.5 0 0 1 5.5 4Z"
        + "M6 7.2H14V8.8H6ZM6 11.2H10.8V12.8H6Z";

    private readonly CanvasControl canvas;
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 14 };
    private (Tool Tool, Style Style, MeasureSettings Measure, int StepStart, bool Locked)? shown;

    /// <summary>The chips, for the tests: switched off as a whole while a locked shape is chosen.</summary>
    internal StackPanel Row => row;
    private Flyout? guide;

    public Button? ColorButton { get; private set; }
    public IReadOnlyList<Button> Swatches { get; private set; } = [];
    public IReadOnlyList<Button> RecentSwatches { get; private set; } = [];

    /// <summary>Custom colours picked lately, for the palette's second row.</summary>
    public Func<IReadOnlyList<string>> RecentColors { get; init; } = () => [];
    public IReadOnlyList<ToggleButton> SizeChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> FillChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> DashChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> FreehandChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> OutsideChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> RatioChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> CornerChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> AlignChips { get; private set; } = [];
    public IReadOnlyList<ToggleButton> OpacityChips { get; private set; } = [];
    public ToggleButton? DifferenceChip { get; private set; }
    public ToggleButton? BoldChip { get; private set; }
    public ToggleButton? AcrossChip { get; private set; }
    public ToggleButton? DownChip { get; private set; }
    public Button? LowerContrast { get; private set; }
    public Button? RaiseContrast { get; private set; }
    public TextBlock? ContrastLabel { get; private set; }
    public IReadOnlyList<ToggleButton> CounterChips { get; private set; } = [];
    public Button? LowerStart { get; private set; }
    public Button? RaiseStart { get; private set; }
    public TextBlock? StartLabel { get; private set; }
    public Button? HelpChip { get; private set; }

    /// <summary>The Measure guide's Got it, while the guide is open.</summary>
    public Button? GuideDone { get; private set; }

    public bool GuideOpen => guide?.IsOpen == true;

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
        tool.HasColor() || tool.HasSize() || tool.HasFill() || tool.HasCorners() || tool.HasOverlay() || tool.HasRatio();

    public void Refresh()
    {
        var session = canvas.Session;
        // A panel stays over the tool and selection it was opened on, and closes when either changes.
        if (mode != StyleBarMode.Tool && panelOpenedOver != (session.Tool, session.Selection))
        {
            mode = StyleBarMode.Tool;
            shown = null;
        }
        if (mode == StyleBarMode.Backdrop)
        {
            RefreshBackdrop();
            return;
        }
        if (mode == StyleBarMode.Size)
        {
            RefreshSize();
            return;
        }
        var selected = session.SelectedAnnotation;
        var tool = selected?.Tool ?? session.Tool;
        var style = selected?.Style ?? session.StyleFor(tool);
        IsVisible = Shows(tool);
        var measure = canvas.MeasureSettings;
        var locked = selected?.IsLocked == true;
        var stepStart = session.Display.StepStart;
        var now = (tool, style, measure, stepStart, locked);
        if (shown == now) return;
        // Only the colour changed: redrawn where it stands. Rebuilding replaced the button the
        // palette hangs from, which closed the palette in the middle of a drag on the spectrum.
        if (shown is { } before && before.Tool == tool && before.Measure == measure
            && before.StepStart == stepStart && before.Locked == locked
            && before.Style with { ColorHex = style.ColorHex } == style)
        {
            shown = now;
            Recolor(style.ColorHex);
            return;
        }
        var picked = shown?.Tool != Tool.Measure && session.Tool == Tool.Measure;
        shown = now;
        Rebuild(tool, style, locked);
        // The guide shows by itself the first time Measure is picked, once the ? it hangs from is in place.
        if (picked && !measure.GuideSeen)
        {
            canvas.ChangeMeasure(m => m with { GuideSeen = true });
            Avalonia.Threading.Dispatcher.UIThread.Post(ShowGuide);
        }
    }

    private void Clear()
    {
        row.Children.Clear();
        // A locked shape dims the tool's chips only; the Backdrop and Size panels are the
        // capture's own, and stay usable.
        row.IsEnabled = true;
        row.Opacity = 1;
        ColorButton = null;
        Swatches = [];
        CustomColor = null;
        colorSwatch = null;
        (DifferenceChip, BoldChip) = (null, null);
        (CounterChips, LowerStart, RaiseStart, StartLabel) = ([], null, null, null);
        (AcrossChip, DownChip, LowerContrast, RaiseContrast, ContrastLabel, HelpChip) = (null, null, null, null, null, null);
        (SizeChips, FillChips, DashChips, FreehandChips, OutsideChips, RatioChips, CornerChips, AlignChips, OpacityChips) =
            ([], [], [], [], [], [], [], [], []);
        (BackdropFillChips, PaddingChips, BackdropCornerChips, ShadowChips) = ([], [], [], []);
        WallpaperNote = null;
        ClearSize();
    }

    /// <summary><paramref name="locked"/>: a locked shape shows its style dimmed, with nothing to
    /// press. Deleting is the layers panel's bin, or the Delete key.</summary>
    private void Rebuild(Tool tool, Style style, bool locked = false)
    {
        Clear();
        if (tool.HasColor()) row.Children.Add(ColorButton = MakeColorButton(style.ColorHex));
        SizeChips = tool.HasSize() ? Group(MakeSizeChips(tool, style)) : [];
        FillChips = tool.HasFill() ? Group(MakeFillChips(tool, style)) : [];
        DashChips = tool.HasDash() ? Group(MakeDashChips(style)) : [];
        FreehandChips = tool.HasFreehand() ? Group(MakeFreehandChips(style)) : [];
        OutsideChips = tool.HasOutsideBlur() ? Group(MakeOutsideChips(style)) : [];
        RatioChips = tool.HasRatio() ? Group(MakeRatioChips(style)) : [];
        CornerChips = tool.HasCorners() ? Group(MakeCornerChips(style)) : [];
        AlignChips = tool.HasAlign() ? Group(MakeAlignChips(style)) : [];
        if (tool.HasBold())
        {
            var bold = Chip(new TextBlock { Text = "B", FontSize = 15, FontWeight = FontWeight.Black }, "Bold", style.Bold,
                            () => canvas.Restyle(s => s with { Bold = !s.Bold }));
            Group([bold]);
            BoldChip = bold;
        }
        if (tool.HasCounter()) AddStepChips(style);
        OpacityChips = tool.HasOpacity() ? Group(MakeOpacityChips(style)) : [];
        if (tool.HasOverlay())
        {
            var difference = Chip(Glyphs.Difference(), "Difference: what matches goes black", style.Difference,
                                  () => canvas.Restyle(s => s with { Difference = !s.Difference }));
            Group([difference]);
            DifferenceChip = difference;
        }
        if (tool == Tool.Measure) AddMeasureChips();
        row.IsEnabled = !locked;
        row.Opacity = locked ? 0.45 : 1;
    }

    // Measure

    private const string AcrossGlyph = "M4 9.2H16V10.8H4ZM4 7H5.6V13H4ZM14.4 7H16V13H14.4Z";
    private const string DownGlyph = "M9.2 4H10.8V16H9.2ZM7 4H13V5.6H7ZM7 14.4H13V16H7Z";
    private const string MinusGlyph = "M5 9.2H15V10.8H5Z";
    private const string PlusGlyph = "M5 9.2H15V10.8H5ZM9.2 5H10.8V15H9.2Z";

    /// <summary>Across and Down, the same as X and Y; the edge contrast as minus, value, plus, the
    /// same as the down and up arrows; and the ? that brings the guide back.</summary>
    private void AddMeasureChips()
    {
        var measure = canvas.MeasureSettings;
        AcrossChip = Chip(Glyphs.Icon(AcrossGlyph, 16), "Across, or X", measure.Across,
                          () => canvas.ChangeMeasure(m => m with { Across = !m.Across }));
        DownChip = Chip(Glyphs.Icon(DownGlyph, 16), "Down, or Y", measure.Down,
                        () => canvas.ChangeMeasure(m => m with { Down = !m.Down }));
        Group([AcrossChip, DownChip]);

        LowerContrast = PlainChip(Glyphs.Icon(MinusGlyph, 14), "Lower edge contrast, finds fainter edges, or the down arrow",
                                  () => canvas.ChangeMeasure(m => m.StepContrast(up: false, coarse: false)));
        RaiseContrast = PlainChip(Glyphs.Icon(PlusGlyph, 14), "Higher edge contrast, finds fewer edges, or the up arrow",
                                  () => canvas.ChangeMeasure(m => m.StepContrast(up: true, coarse: false)));
        ContrastLabel = new TextBlock
        {
            Text = measure.ContrastLabel,
            Width = 34,
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFeatures = FontFeatureCollection.Parse("tnum"),
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush"),
        };
        ToolTip.SetTip(ContrastLabel, "Edge contrast: how big a change in brightness counts as an edge");
        AutomationProperties.SetName(ContrastLabel, $"Edge contrast {measure.ContrastLabel}");
        row.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children = { LowerContrast, ContrastLabel, RaiseContrast },
        });

        var mark = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1.4),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("SystemControlForegroundBaseHighBrush"),
            Child = new TextBlock
            {
                Text = "?",
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        HelpChip = PlainChip(mark, "Show the Measure guide", ShowGuide);
        row.Children.Add(HelpChip);
    }

    // Steps

    /// <summary>1 2 3 or A B C, then minus, where this capture's steps start, plus.</summary>
    private void AddStepChips(Style style)
    {
        CounterChips = Group([.. new[] { false, true }.Select(letters =>
        {
            var chip = Chip(new TextBlock { Text = letters ? "ABC" : "123", FontSize = 11, FontWeight = FontWeight.SemiBold },
                            letters ? "Count A, B, C" : "Count 1, 2, 3", style.Letters == letters,
                            () => canvas.Restyle(s => s with { Letters = letters }));
            chip.Width = 40;
            return chip;
        })]);
        var start = canvas.Session.Display.StepStart;
        LowerStart = PlainChip(Glyphs.Icon(MinusGlyph, 14), "Start one lower", () => canvas.SetStepStart(start - 1));
        RaiseStart = PlainChip(Glyphs.Icon(PlusGlyph, 14), "Start one higher", () => canvas.SetStepStart(start + 1));
        StartLabel = new TextBlock
        {
            Text = $"From {start}",
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFeatures = FontFeatureCollection.Parse("tnum"),
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush"),
        };
        ToolTip.SetTip(StartLabel, "Where this capture's steps start counting");
        AutomationProperties.SetName(StartLabel, $"Steps start from {start}");
        row.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children = { LowerStart, StartLabel, RaiseStart },
        });
    }

    private static Button PlainChip(Control glyph, string tip, Action pick)
    {
        var chip = new Button
        {
            Content = glyph,
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

    /// <summary>What the Measure tool does: every key beside the chip that does the same thing.
    /// Clicks pass through it, so measuring can start while it is up.</summary>
    private void ShowGuide()
    {
        if (HelpChip is not { } anchor || TopLevel.GetTopLevel(anchor) is null) return;
        GuideDone = new Button { Content = "Got it", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var keys = new Grid { ColumnDefinitions = new ColumnDefinitions("58,*"), RowSpacing = 10, ColumnSpacing = 12 };
        (string Keys, string Note)[] rows =
        [
            ("X", "Across the space, or the Across chip"),
            ("Y", "Down it, or the Down chip. Both at once show both"),
            ("Click", "Keeps the reading on the capture"),
            ("↑ ↓", "Finds more or fewer edges, when one is missed"),
        ];
        for (var index = 0; index < rows.Length; index++)
        {
            keys.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cap = new Border
            {
                Padding = new Thickness(6, 2),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = rows[index].Keys, FontSize = 12, FontWeight = FontWeight.Medium },
                [!Border.BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundBaseLowBrush"),
            };
            var note = new TextBlock
            {
                Text = rows[index].Note,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush"),
            };
            Grid.SetRow(cap, index);
            Grid.SetRow(note, index);
            Grid.SetColumn(note, 1);
            keys.Children.Add(cap);
            keys.Children.Add(note);
        }
        var footnote = new TextBlock
        {
            Text = "The ? brings this back",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush"),
        };
        guide = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            OverlayDismissEventPassThrough = true,
            Content = new StackPanel
            {
                Width = 288,
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "Measure the space under the pointer", FontSize = 13, FontWeight = FontWeight.SemiBold },
                    keys,
                    new DockPanel { Children = { GuideDone, footnote } },
                },
            },
        };
        DockPanel.SetDock(GuideDone, Dock.Right);
        var shown = guide;
        GuideDone.Click += (_, _) => shown.Hide();
        guide.ShowAt(anchor);
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
        Button Swatch(string swatch)
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
        }
        var swatches = Palette.Swatches.Select(Swatch).ToArray();
        Swatches = swatches;
        // The custom colours picked lately, in a row under the fixed ones, read again each time
        // the palette opens: one picked a moment ago is there the next time.
        var recentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        AutomationProperties.SetName(recentRow, "Recent colours");
        void FillRecent()
        {
            RecentSwatches = [.. RecentColors().Select(Swatch)];
            recentRow.Children.Clear();
            foreach (var swatch in RecentSwatches) recentRow.Children.Add(swatch);
            recentRow.IsVisible = RecentSwatches.Count > 0;
        }
        FillRecent();

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
            ApplyColor(picked, merging: true);
        };
        var palette = new StackPanel { Spacing = 10, Margin = new Thickness(4), Children = { grid, recentRow, field, CustomColor } };

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
        button.Flyout.Opened += (_, _) => FillRecent();
        // A spectrum drag is remembered once, when the palette closes.
        button.Flyout.Closed += (_, _) =>
        {
            if (mode == StyleBarMode.Backdrop && canvas.Session.Display.Backdrop is { } backdrop) Remember?.Invoke(backdrop);
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
        ApplyColor(hex, merging: false);
    }

    /// <summary>The backdrop's colour while its panel is open, otherwise the tool's or selection's.</summary>
    private void ApplyColor(string hex, bool merging)
    {
        if (mode == StyleBarMode.Backdrop)
            ChangeBackdrop(backdrop => backdrop with { ColorHex = hex }, merging);
        else
            canvas.Restyle(style => style with { ColorHex = hex }, merging);
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
        [.. new[] { false, true }.Select(filled => tool == Tool.Text
            ? Chip(Glyphs.Icon(filled ? TextOnABoxGlyph : PlainTextGlyph, 16), filled ? "Text on a box" : "Plain text",
                   style.Filled == filled, () => canvas.Restyle(s => s with { Filled = filled }))
            : Chip(Glyphs.Box(oval: tool == Tool.Oval, filled), filled ? "Filled" : "Outline", style.Filled == filled,
                   () => canvas.Restyle(s => s with { Filled = filled })))];

    /// <summary>A solid line and a dashed one, as the Mac's chips draw them.</summary>
    private static readonly string[] DashGlyphs =
        ["M4 9.2H16V10.8H4Z", "M4 9.2H6.4V10.8H4ZM8.8 9.2H11.2V10.8H8.8ZM13.6 9.2H16V10.8H13.6Z"];

    private List<ToggleButton> MakeDashChips(Style style) =>
        [.. new[] { false, true }.Select(dashed =>
            Chip(Glyphs.Icon(DashGlyphs[dashed ? 1 : 0], 16), dashed ? "Dashed" : "Solid", style.Dashed == dashed,
                 () => canvas.Restyle(s => s with { Dashed = dashed })))];

    /// <summary>A straight stroke, and one that wanders as the pointer did, as the Mac's chips draw them.</summary>
    private static readonly string[] FreehandGlyphs =
        ["M4.4 12.6L15 6.6L15.6 7.7L5 13.7Z", "M4 12.4C8 3.4 12 16.6 16 7.2L16 8.8C12 18.6 8 5.4 4 14Z"];

    private List<ToggleButton> MakeFreehandChips(Style style) =>
        [.. new[] { false, true }.Select(freehand =>
            Chip(Glyphs.Icon(FreehandGlyphs[freehand ? 1 : 0], 16), freehand ? "Freehand: follows the pointer" : "Straight",
                 style.Freehand == freehand, () => canvas.Restyle(s => s with { Freehand = freehand })))];

    /// <summary>A frame round a clear middle: solid for dimming, broken into dots for blurring, as
    /// the Mac's chips draw them.</summary>
    private static readonly string[] OutsideGlyphs =
    [
        "F0 M4 5H16V15H4ZM8.5 8.5V11.5H11.5V8.5Z",
        "M4 5H6V7H4ZM7 5H9V7H7ZM11 5H13V7H11ZM14 5H16V7H14ZM4 9H6V11H4ZM14 9H16V11H14ZM4 13H6V15H4ZM7 13H9V15H7ZM11 13H13V15H11ZM14 13H16V15H14Z",
    ];

    private List<ToggleButton> MakeOutsideChips(Style style) =>
        [.. new[] { false, true }.Select(blur =>
            Chip(Glyphs.Icon(OutsideGlyphs[blur ? 1 : 0], 16), blur ? "Blur what is outside" : "Dim what is outside",
                 style.BlurOutside == blur, () => canvas.Restyle(s => s with { BlurOutside = blur })))];

    /// <summary>Free, 1:1, 4:3 and 16:9, written out.</summary>
    private List<ToggleButton> MakeRatioChips(Style style) =>
        [.. Enum.GetValues<CropRatio>().Select(ratio =>
        {
            var chip = Chip(new TextBlock { Text = ratio == CropRatio.Free ? "Free" : ratio.Wire(), FontSize = 11, FontWeight = FontWeight.SemiBold },
                            ratio == CropRatio.Free ? "Crop to any shape" : $"Crop to {ratio.Wire()}", style.CropRatio == ratio,
                            () => canvas.Restyle(s => s with { CropRatio = ratio }));
            chip.Width = 42;
            return chip;
        })];

    private List<ToggleButton> MakeAlignChips(Style style) =>
        [.. Enum.GetValues<TextAlign>().Select((align, index) =>
            Chip(Glyphs.Icon(AlignGlyphs[index], 16), AlignNames[index], style.Align == align,
                 () => canvas.Restyle(s => s with { Align = align })))];

    private List<ToggleButton> MakeCornerChips(Style style) =>
        [.. Enum.GetValues<CornerSize>().Select((corners, index) =>
            Chip(Glyphs.Box(oval: false, filled: false, radius: corners == CornerSize.Full ? 6 : new[] { 0, 1.5, 3, 5 }[index]),
                 CornerNames[index], style.Corners == corners, () => canvas.Restyle(s => s with { Corners = corners })))];

    private List<ToggleButton> MakeOpacityChips(Style style) =>
        [.. Opacities.Select(opacity =>
            Chip(Glyphs.Faded(opacity), $"Opacity {(int)(opacity * 100)}%, or keys 1 to 9 and 0",
                 Math.Abs(style.Opacity - opacity) < 0.01, () => canvas.Restyle(s => s with { Opacity = opacity })))];
}
