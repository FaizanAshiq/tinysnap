using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Tinysnap.Core;
using Size = Avalonia.Size;

namespace Tinysnap.App.Library;

/// <summary>The selected capture at its size, no larger than 80% of the screen, for a look
/// without opening it. Space or Esc closes it; the arrows step through the library.</summary>
internal sealed class PreviewWindow : Window
{
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private Bitmap? shown;

    /// <summary>An arrow key, for the library to move its selection.</summary>
    public event Action<Key>? StepRequested;

    public PreviewWindow()
    {
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = image;
        Closed += (_, _) => shown?.Dispose();
    }

    /// <summary>Shows <paramref name="entry"/>'s rendered image, sized to its points.</summary>
    public void Show(LibraryEntry entry)
    {
        Title = $"Capture at {entry.Captured.ToLocalTime():G}";
        var path = File.Exists(entry.ImagePath) ? entry.ImagePath : entry.OriginalPath;
        Bitmap? next;
        try
        {
            next = new Bitmap(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            next = null;
        }
        shown?.Dispose();
        shown = next;
        image.Source = next;
        if (next is null) return;
        // Tinysnap writes 72 DPI per point, so the DPI gives the capture's scale.
        var scale = Math.Max(1, next.Dpi.X / 72);
        var points = new Size(next.PixelSize.Width / scale, next.PixelSize.Height / scale);
        var work = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea;
        var scaling = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.Scaling ?? 1;
        var room = work is { } area ? new Size(area.Width / scaling * 0.8, area.Height / scaling * 0.8) : new Size(1200, 800);
        var fit = Math.Min(1, Math.Min(room.Width / points.Width, room.Height / points.Height));
        Width = points.Width * fit;
        Height = points.Height * fit;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space or Key.Escape:
                Close();
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                StepRequested?.Invoke(e.Key);
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
    }
}
