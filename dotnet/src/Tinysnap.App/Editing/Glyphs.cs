using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace Tinysnap.App.Editing;

/// <summary>The small drawings on the style bar's chips, each showing what it gives, in the
/// theme's text colour.</summary>
internal static class Glyphs
{
    private const string Ink = "SystemControlForegroundBaseHighBrush";

    public static Control Icon(string pathData, double size = 18) => new PathIcon
    {
        Data = Geometry.Parse(pathData),
        Width = size,
        Height = size,
    };

    public static Control Line(double thickness) => Inked(new Avalonia.Controls.Shapes.Line
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(14, 0),
        StrokeThickness = thickness,
        StrokeLineCap = PenLineCap.Round,
        VerticalAlignment = VerticalAlignment.Center,
    }, stroke: true);

    public static Control Dot(double diameter) => Inked(new Ellipse { Width = diameter, Height = diameter }, stroke: false);

    public static Control Letter(double size) =>
        new TextBlock { Text = "A", FontSize = size, FontWeight = FontWeight.SemiBold, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension(Ink) };

    public static Control Box(bool oval, bool filled, double radius = 3) => oval
        ? Inked(new Ellipse { Width = 16, Height = 12, StrokeThickness = 1.6 }, stroke: !filled)
        : Inked(new Rectangle { Width = 16, Height = 12, RadiusX = radius, RadiusY = radius, StrokeThickness = 1.6 }, stroke: !filled);

    public static Control Faded(double opacity) =>
        new Rectangle { Width = 14, Height = 12, RadiusX = 2, RadiusY = 2, Opacity = opacity, [!Shape.FillProperty] = new DynamicResourceExtension(Ink) };

    /// <summary>Two overlapping squares with their overlap cut out, the difference blend's look.</summary>
    public static Control Difference() => Inked(new Avalonia.Controls.Shapes.Path
    {
        Data = new GeometryGroup
        {
            FillRule = FillRule.EvenOdd,
            Children = { new RectangleGeometry(new Rect(0, 3, 11, 9)), new RectangleGeometry(new Rect(5, 0, 11, 9)) },
        },
    }, stroke: false);

    private static Shape Inked(Shape shape, bool stroke)
    {
        shape[!(stroke ? Shape.StrokeProperty : Shape.FillProperty)] = new DynamicResourceExtension(Ink);
        return shape;
    }
}
