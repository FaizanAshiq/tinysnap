using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Tinysnap.Core;
using Size = Tinysnap.Core.Size;

namespace Tinysnap.App.Editing;

/// <summary>The Size panel: 25, 50, 100 and 200%, the one in use lit whether it was picked or comes
/// from the Export setting, then the pixels that size makes, which can be typed over.</summary>
internal sealed partial class StyleBar
{
    private static readonly double[] SizeChoices = [0.25, 0.5, 1, 2];

    private (double Fraction, Size Pixels)? shownSize;

    /// <summary>The Export setting, which a capture with no size of its own follows.</summary>
    public Func<ExportScale> ExportSetting { get; set; } = () => ExportScale.Native;

    public IReadOnlyList<ToggleButton> ExportSizeChips { get; private set; } = [];
    public TextBox? WidthField { get; private set; }
    public TextBox? HeightField { get; private set; }

    private void RefreshSize()
    {
        IsVisible = true;
        var display = canvas.Session.Display;
        var fraction = Exporter.OutputScale(display, ExportSetting());
        var pixels = display.ExportPixelSize(fraction);
        if (shownSize == (fraction, pixels)) return;
        shownSize = (fraction, pixels);
        Clear();
        ExportSizeChips = Group([.. SizeChoices.Select(choice =>
        {
            var title = FormattableString.Invariant($"{choice * 100:0}%");
            var chip = Chip(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold }, $"Export at {title}",
                            Math.Abs(fraction - choice) < 0.0005, () => canvas.SetResize(choice));
            chip.Width = 46;
            return chip;
        })]);
        WidthField = PixelField(pixels.Width, "Export width in pixels, Enter to apply", width => display.ResizeForWidth(width));
        HeightField = PixelField(pixels.Height, "Export height in pixels, Enter to apply", height => display.ResizeForHeight(height));
        row.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { WidthField, Muted("×"), HeightField, Muted("px") },
        });
    }

    private void ClearSize()
    {
        ExportSizeChips = [];
        (WidthField, HeightField) = (null, null);
    }

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush"),
    };

    /// <summary>A whole number of pixels, entered, sets the size that makes it, held to the limits,
    /// and the fields then show what it gave. Anything else puts the size in use back, selected to
    /// type over.</summary>
    private TextBox PixelField(double value, string label, Func<int, double> resizeFor)
    {
        var current = ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var field = new TextBox
        {
            Text = current,
            Width = 64,
            FontSize = 12,
            TextAlignment = TextAlignment.Right,
            FontFeatures = FontFeatureCollection.Parse("tnum"),
        };
        ToolTip.SetTip(field, label);
        Avalonia.Automation.AutomationProperties.SetName(field, label);
        field.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Return)) return;
            e.Handled = true;
            if (int.TryParse(field.Text?.Trim(), out var pixels) && pixels > 0)
            {
                canvas.SetResize(resizeFor(pixels));
                shownSize = null;
                Refresh();
                return;
            }
            field.Text = current;
            field.SelectAll();
        }, RoutingStrategies.Tunnel);
        return field;
    }
}
