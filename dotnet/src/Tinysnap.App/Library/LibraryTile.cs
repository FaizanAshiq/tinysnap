using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Tinysnap.App.Editing;
using Tinysnap.Core;

namespace Tinysnap.App.Library;

/// <summary>One capture: its picture, the time it was taken and its size in pixels. While the
/// pointer is on it a dark fade rises over the picture and Copy, Save and Edit slide up from its
/// foot; only those buttons take clicks, so the rest of the picture still selects, opens and
/// drags as usual.</summary>
internal sealed class LibraryTile : Border
{
    private static readonly TimeSpan In = TimeSpan.FromSeconds(0.24), Out = TimeSpan.FromSeconds(0.16);
    // The same kind of step at both ends, so the slide interpolates between them.
    private static readonly ITransform Lowered = TransformOperations.Parse("translateY(40px)");
    private static readonly ITransform Raised = TransformOperations.Parse("translateY(0px)");

    private readonly Border fade;
    private readonly Grid actions;
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private bool isSelected;

    public LibraryEntry Entry { get; }

    internal bool IsHovered { get; private set; }

    /// <summary>Copy, Save and Edit, in that order.</summary>
    internal IReadOnlyList<Button> HoverButtons { get; }

    /// <summary>The hour and minute it was taken, under the picture.</summary>
    internal TextBlock TimeLabel { get; }

    public event Action<Button>? CopyRequested;
    public event Action<Button>? SaveRequested;
    public event Action? EditRequested;

    /// <summary>The picture, which arrives after the tile: the library decodes it in the background.</summary>
    internal Bitmap? Picture
    {
        get => image.Source as Bitmap;
        set => image.Source = value;
    }

    public LibraryTile(LibraryEntry entry, PixelSize pixels)
    {
        Entry = entry;
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(3);
        BorderBrush = Brushes.Transparent;
        this[!BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundListLowBrush");
        var taken = entry.Captured.ToLocalTime().ToString("t");
        AutomationProperties.SetName(this, $"Capture at {taken}, {pixels.Width} by {pixels.Height} pixels");
        // Focus moves with the selection, so a screen reader reads each capture out as the arrows reach
        // it. The selection border already shows where it is.
        Focusable = true;
        FocusAdorner = null;

        fade = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0,
            // Strongest under the buttons, gone by two thirds of the way up, so white labels read
            // over a white capture as well as a dark one. The Mac's stops.
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(199, 0, 0, 0), 0),
                    new GradientStop(Color.FromArgb(115, 0, 0, 0), 0.38),
                    new GradientStop(Colors.Transparent, 0.7),
                },
            },
        };
        var copy = Action(ToolIcons.Copy, "Copy", "Copy", primary: true);
        var save = Action(ToolIcons.Save, "Save", "Save to the save folder", primary: false);
        var edit = Action(ToolIcons.For(Tool.Freehand), "Edit", "Edit", primary: false);
        copy.Click += (_, _) => CopyRequested?.Invoke(copy);
        save.Click += (_, _) => SaveRequested?.Invoke(save);
        edit.Click += (_, _) => EditRequested?.Invoke();
        HoverButtons = [copy, save, edit];
        // Three equal buttons across the picture, as on the Mac.
        actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 5,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(6, 0, 6, 8),
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = Lowered,
            Children = { copy, save, edit },
        };
        Grid.SetColumn(save, 1);
        Grid.SetColumn(edit, 2);

        var well = new Border
        {
            Margin = new Thickness(8, 8, 8, 0),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            // A well a shade under the tile, the same box on every tile, so a wide capture's
            // bands read as part of the frame rather than as uneven space.
            Background = new SolidColorBrush(Color.FromArgb(31, 0, 0, 0)),
            Child = new Panel
            {
                Children =
                {
                    image,
                    fade,
                    actions,
                },
            },
        };
        TimeLabel = Label(taken, HorizontalAlignment.Left);
        var size = Label($"{pixels.Width} × {pixels.Height}", HorizontalAlignment.Right);
        var labels = new Grid { Margin = new Thickness(10, 0), Children = { TimeLabel, size } };
        Grid.SetRow(labels, 1);
        Child = new Grid { RowDefinitions = new RowDefinitions("*,32"), Children = { well, labels } };

        PointerEntered += (_, _) => ShowActions(true);
        PointerExited += (_, _) => ShowActions(false);
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            isSelected = value;
            BorderBrush = value ? new SolidColorBrush(Accent.Color) : Brushes.Transparent;
        }
    }

    private static TextBlock Label(string text, HorizontalAlignment alignment) => new()
    {
        Text = text,
        FontSize = 12,
        FontFeatures = FontFeatureCollection.Parse("tnum"),
        HorizontalAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Center,
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush"),
    };

    private static Button Action(string icon, string label, string tip, bool primary)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { Glyphs.Icon(icon, 14), new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        button.Classes.Add("tile-action");
        if (primary) button.Classes.Add("primary");
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>The fade and the buttons come up in about a quarter of a second, easing out, and go
    /// down a little quicker.</summary>
    private void ShowActions(bool shown)
    {
        IsHovered = shown;
        var duration = shown ? In : Out;
        fade.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = new CubicEaseOut() }];
        actions.Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = duration, Easing = new CubicEaseOut() },
        ];
        fade.Opacity = shown ? 1 : 0;
        actions.Opacity = shown ? 1 : 0;
        actions.RenderTransform = shown ? Raised : Lowered;
        actions.IsHitTestVisible = shown;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TilePeer(this);

    /// <summary>A list item, named for its capture: a plain border is left out of the accessibility tree.</summary>
    private sealed class TilePeer(LibraryTile tile) : ControlAutomationPeer(tile)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
    }
}
