using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Tinysnap.Core;
using AvaloniaPoint = Avalonia.Point;
using Style = Avalonia.Styling.Style;
using Thickness = Avalonia.Thickness;

namespace Tinysnap.App.Editing;

/// <summary>A text field sits over the annotation while it is typed, in the renderer's own font
/// at its size on screen, and the renderer leaves its letters out, drawing only a filled text's
/// box, so nothing is drawn twice.</summary>
internal sealed partial class CanvasControl
{
    private TextBox? textBox;

    private AvaloniaPoint TextOrigin { get; set; }

    private void SyncTextBox()
    {
        if (Session.TypingId is not { } id
            || Session.Display.Annotation(id) is not { Kind: AnnotationKind.Text(var origin, var text) } annotation)
        {
            if (textBox is null) return;
            LogicalChildren.Remove(textBox);
            VisualChildren.Remove(textBox);
            textBox = null;
            Focus();
            return;
        }

        var points = annotation.PixelSize(Session.Scale) / Session.Scale;
        var box = textBox ?? MakeTextBox();
        using (var font = TextLayout.Font(points)) box.FontFamily = new FontFamily(font.Typeface.FamilyName);
        box.FontSize = points * zoom;
        box.MinWidth = points * zoom;
        var brush = new SolidColorBrush(Color.FromUInt32(ToArgb(TextLayout.LetterColor(annotation.Style))));
        box.Foreground = brush;
        box.CaretBrush = brush;
        // The box is as wide as its widest line, as the renderer's text box is, so its lines
        // line up the same way inside it.
        box.TextAlignment = annotation.Style.Align switch
        {
            TextAlign.Center => TextAlignment.Center,
            TextAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        if (box.Text != text) box.Text = text;
        var at = Mapping.ToDips(origin);
        TextOrigin = new AvaloniaPoint(at.X, at.Y);
        InvalidateArrange();
        if (!box.IsFocused) box.Focus();
    }

    private static uint ToArgb(SkiaSharp.SKColor color) =>
        (uint)color.Alpha << 24 | (uint)color.Red << 16 | (uint)color.Green << 8 | color.Blue;

    private TextBox MakeTextBox()
    {
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
        };
        // No field chrome in any state: the text looks as it will once drawn.
        box.Styles.Add(new Style(selector => selector.OfType<TextBox>().Template().OfType<Border>())
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, Brushes.Transparent),
                new Setter(Border.BorderThicknessProperty, new Thickness(0)),
            },
        });
        box.Styles.Add(new Style(selector => selector.OfType<TextBox>().Template().OfType<ScrollContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.PaddingProperty, new Thickness(0)) },
        });
        box.TextChanged += (_, _) =>
        {
            if (Session.TypingId is null) return;
            Session.UpdateTyping(box.Text ?? "");
            SessionChanged();
        };
        LogicalChildren.Add(box);
        VisualChildren.Add(box);
        textBox = box;
        return box;
    }
}
