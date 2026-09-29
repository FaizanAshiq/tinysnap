using Avalonia;
using Avalonia.Media;

namespace Tinysnap.App;

/// <summary>The system's accent colour, for selections, borders and handles, as the Mac uses its
/// own: Windows' accent where the theme offers it, its default blue otherwise.</summary>
internal static class Accent
{
    public static Color Color =>
        Application.Current is { } app && app.TryGetResource("SystemAccentColor", app.ActualThemeVariant, out var value)
        && value is Color color
            ? color
            : Color.FromRgb(0, 120, 212);
}
