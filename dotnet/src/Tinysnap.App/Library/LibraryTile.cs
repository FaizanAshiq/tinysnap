using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
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
    private static readonly ITransform Lowered = TransformOperations.Parse("translateY(40px)");

    private readonly Border fade;
    private readonly StackPanel actions;
    private bool isSelected;

    public LibraryEntry Entry { get; }

    internal bool IsHovered { get; private set; }

    /// <summary>Copy, Save and Edit, in that order.</summary>
    internal IReadOnlyList<Button> HoverButtons { get; }

    public event Action<Button>? CopyRequested;
    public event Action<Button>? SaveRequested;
    public event Action? EditRequested;

    public LibraryTile(LibraryEntry entry, Bitmap? picture, PixelSize pixels)
    {
        Entry = entry;
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(2);
        BorderBrush = Brushes.Transparent;
        this[!BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundBaseLowBrush");
        AutomationProperties.SetName(this, $"Capture at {entry.Captured.ToLocalTime():T}");

        fade = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0,
            // Strongest under the buttons, gone by two thirds of the way up, so white labels read
            // over a white capture as well as a dark one.
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1.0 / 3, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(166, 0, 0, 0), 0), new GradientStop(Colors.Transparent, 1) },
            },
        };
        var copy = Action(ToolIcons.Copy, "Copy", "Copy", primary: true);
        var save = Action(ToolIcons.Save, "Save", "Save to the save folder", primary: false);
        var edit = Action(ToolIcons.For(Tool.Freehand), "Edit", "Edit", primary: false);
        copy.Click += (_, _) => CopyRequested?.Invoke(copy);
        save.Click += (_, _) => SaveRequested?.Invoke(save);
        edit.Click += (_, _) => EditRequested?.Invoke();
        HoverButtons = [copy, save, edit];
        actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = Lowered,
            Children = { copy, save, edit },
        };

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
                    new Image { Source = picture, Stretch = Stretch.Uniform },
                    fade,
                    actions,
                },
            },
        };
        var time = Label(entry.Captured.ToLocalTime().ToString("T"), HorizontalAlignment.Left);
        var size = Label($"{pixels.Width} × {pixels.Height}", HorizontalAlignment.Right);
        var labels = new Grid { Margin = new Thickness(10, 0), Children = { time, size } };
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
        actions.RenderTransform = shown ? TransformOperations.Identity : Lowered;
        actions.IsHitTestVisible = shown;
    }
}
