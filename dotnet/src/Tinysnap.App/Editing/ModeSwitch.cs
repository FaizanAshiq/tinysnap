using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Tinysnap.Core;

namespace Tinysnap.App.Editing;

/// <summary>Essential or Pro side by side, the chosen one filled with the accent: the editor's two
/// toolbars, one click apart. The toolbar keeps it in view at any width.</summary>
internal sealed class ModeSwitch : Border
{
    public ToggleButton Essential { get; } = Segment("Essential", "The everyday tools");
    public ToggleButton Pro { get; } = Segment("Pro", "Every tool, grouped by kind");

    public EditorMode Mode { get; private set; }

    /// <summary>A mode picked here, not one shown for a choice made elsewhere.</summary>
    public event Action<EditorMode>? Chosen;

    public ModeSwitch(EditorMode mode)
    {
        CornerRadius = new CornerRadius(15);
        Padding = new Thickness(2);
        VerticalAlignment = VerticalAlignment.Center;
        this[!BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundBaseLowBrush");
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { Essential, Pro } };
        Essential.Click += (_, _) => Choose(EditorMode.Essential);
        Pro.Click += (_, _) => Choose(EditorMode.Pro);
        Show(mode);
    }

    /// <summary>Shows <paramref name="mode"/> as chosen, telling no one.</summary>
    public void Show(EditorMode mode)
    {
        Mode = mode;
        // A click toggles the button it lands on, the chosen one included: set again every time.
        Essential.IsChecked = mode == EditorMode.Essential;
        Pro.IsChecked = mode == EditorMode.Pro;
    }

    private void Choose(EditorMode mode)
    {
        var changed = mode != Mode;
        Show(mode);
        if (changed) Chosen?.Invoke(mode);
    }

    private static ToggleButton Segment(string label, string tip)
    {
        var segment = new ToggleButton
        {
            Content = label,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            MinHeight = 24,
            Padding = new Thickness(12, 3),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(segment, tip);
        AutomationProperties.SetName(segment, label);
        return segment;
    }
}
