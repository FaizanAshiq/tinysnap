using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Tinysnap.App.Editing;
using Tinysnap.App.Pinning;
using Tinysnap.Core;
using Size = Tinysnap.Core.Size;

namespace Tinysnap.App.Capturing;

/// <summary>A capture floating in the bottom right corner of its monitor instead of opening the
/// editor. A click edits it, dragging drops it into another app, the buttons copy, save or pin
/// it. Left alone for 5 seconds, or swiped right, it slides away, kept in the library; with the
/// library off it lands on the clipboard instead, so it is never lost.</summary>
internal sealed class CaptureThumbnail : Window
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly Document document;
    /// <summary>The library entry that keeps the capture, so the thumbnail need not copy it.</summary>
    private readonly LibraryEntry? entry;
    private readonly EditorServices services;
    private readonly PixelPoint? on;
    private readonly bool animate;
    private readonly SharedImage? picture;
    private readonly Size size;
    private readonly ITimer timer;
    /// <summary>The clipboard as it was when the thumbnail appeared. A time out copies only if it
    /// is still so, rather than replace text or an image copied since.</summary>
    private readonly uint clipboardCount;
    private readonly StackPanel actions;
    private readonly Button closeButton;
    private PointerPressedEventArgs? pressed;
    private Avalonia.Point pressedAt;
    private double swipe;

    /// <summary>A click on it: the controller opens an editor on the document.</summary>
    public event Action<Document>? OpenRequested;

    internal bool IsGone { get; private set; }

    /// <summary>Copy, Save, Pin and the close button, shown while the pointer is over it.</summary>
    internal IReadOnlyList<Button> Buttons { get; }

    /// <param name="on">A point on the monitor the capture came from, in physical pixels.</param>
    /// <param name="animate">False when the person asked for less motion: it appears and goes
    /// without sliding.</param>
    public CaptureThumbnail(Document document, EditorServices services, PixelPoint? on = null, bool animate = true,
                            TimeProvider? time = null, LibraryEntry? entry = null)
    {
        this.document = document;
        this.entry = entry;
        this.services = services;
        this.on = on;
        this.animate = animate;
        clipboardCount = services.Clipboard.ChangeCount;
        size = ThumbnailGeometry.Fit(document.Capture.PointSize);
        timer = (time ?? TimeProvider.System).CreateTimer(_ => Dispatcher.Post(() => Dismiss(copying: true)),
                                                          null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        Title = "Capture thumbnail";
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Width = size.Width + PinGeometry.Shadow * 2;
        Height = size.Height + PinGeometry.Shadow * 2;

        if (Exporter.Export(document, ExportScale.Native) is { } exported) picture = new SharedImage(exported.Image);

        actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 8),
            IsVisible = false,
        };
        var buttons = new List<Button>();
        foreach (var (title, action) in new (string, Action)[] { ("Copy", Copy), ("Save", Save), ("Pin", Pin) })
        {
            var button = new Button { Content = title, FontSize = 12, Padding = new Thickness(10, 3) };
            AutomationProperties.SetName(button, title);
            button.Click += (_, _) => action();
            actions.Children.Add(button);
            buttons.Add(button);
        }
        closeButton = new Button
        {
            Content = Glyphs.Icon(ToolIcons.Close),
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            IsVisible = false,
        };
        AutomationProperties.SetName(closeButton, "Close without copying");
        ToolTip.SetTip(closeButton, "Close without copying");
        closeButton.Click += (_, _) => Dismiss(copying: false);
        buttons.Add(closeButton);
        Buttons = buttons;

        var card = new Border
        {
            Width = size.Width,
            Height = size.Height,
            Margin = new Thickness(PinGeometry.Shadow),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            BorderThickness = new Thickness(0.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 4, Blur = 16, Color = Color.FromArgb(90, 0, 0, 0) }),
            Background = Brushes.Transparent,
            Child = new Grid
            {
                Children = { picture is null ? new Panel() : new FilledImage(picture), actions, closeButton },
            },
        };
        AutomationProperties.SetName(card, "Capture thumbnail");
        AutomationProperties.SetHelpText(card, "Click to edit, drag into another app, or wait to copy it");
        card.PointerEntered += (_, _) => Hover(true);
        card.PointerExited += (_, _) => Hover(false);
        card.PointerPressed += Press;
        card.PointerMoved += Move;
        card.PointerReleased += Release;
        Content = card;

        Opened += (_, _) =>
        {
            SlideIn();
            timer.Change(Wait, Timeout.InfiniteTimeSpan);
        };
        Closed += (_, _) =>
        {
            timer.Dispose();
            picture?.Release();
        };
    }

    // Leaving

    /// <summary>Every way out comes through here. <paramref name="copying"/> is the time out, the
    /// swipe and a new capture, which copy only when the library did not keep the capture, and
    /// only onto an unchanged clipboard; the close button, a click to edit, the buttons and a
    /// drag out leave the clipboard alone.</summary>
    public void Dismiss(bool copying)
    {
        if (IsGone) return;
        IsGone = true;
        timer.Dispose();
        if (copying && entry is null && services.Clipboard.ChangeCount == clipboardCount) CopyNow();
        Slide(Position, new PixelPoint(Position.X + (int)(Bounds.Width * DesktopScaling) + 40, Position.Y), Close);
    }

    private void Hover(bool inside)
    {
        actions.IsVisible = inside;
        closeButton.IsVisible = inside;
        if (IsGone) return;
        timer.Change(inside ? Timeout.InfiniteTimeSpan : Wait, Timeout.InfiniteTimeSpan);
    }

    private (ExportedImage Exported, byte[] Png)? Exported(ExportScale scale)
    {
        if (Output.Export(document, scale) is { } output) return output;
        _ = services.Dialogs.Tell(null, "Tinysnap could not draw this capture.");
        return null;
    }

    private void CopyNow()
    {
        if (Exported(services.Preferences().ExportScale) is not var (exported, png)) return;
        using var image = exported.Image;
        if (!Output.Copy(services.Clipboard, exported, png))
            _ = services.Dialogs.Tell(null, "Tinysnap could not copy to the clipboard. Another app may be holding it; try again.");
    }

    /// <summary>Asked for, so it copies whatever the clipboard holds now.</summary>
    internal void Copy()
    {
        CopyNow();
        Dismiss(copying: false);
    }

    internal void Save()
    {
        if (Exported(services.Preferences().ExportScale) is not var (exported, png)) return;
        exported.Image.Dispose();
        try
        {
            Output.Save(png, services.Preferences().SaveFolderPath, DateTimeOffset.Now);
            Dismiss(copying: false);
        }
        catch (OutputException error)
        {
            _ = services.Dialogs.Tell(null, error.Message);
        }
    }

    internal void Pin()
    {
        if (services.Pin is not { } pin || Exported(ExportScale.Native) is not var (exported, _)) return;
        pin(exported, document.Resize is not null, entry);
        Dismiss(copying: false);
    }

    private void Open()
    {
        OpenRequested?.Invoke(document);
        Dismiss(copying: false);
    }

    // Pointer

    private void Press(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        pressed = e;
        pressedAt = e.GetPosition(this);
        e.Handled = true;
    }

    /// <summary>A few points of movement makes it a drag out rather than a click.</summary>
    private async void Move(object? sender, PointerEventArgs e)
    {
        if (pressed is not { } start) return;
        var at = e.GetPosition(this);
        if (Math.Sqrt(Math.Pow(at.X - pressedAt.X, 2) + Math.Pow(at.Y - pressedAt.Y, 2)) <= 4) return;
        pressed = null;
        if (Exported(services.Preferences().ExportScale) is not var (exported, png)) return;
        exported.Image.Dispose();
        if (Output.TemporaryFile(png, DateTimeOffset.Now) is not { } path
            || await StorageProvider.TryGetFileFromPathAsync(path) is not { } file) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(file));
        if (await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Copy) != DragDropEffects.None) Dismiss(copying: false);
    }

    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        if (pressed is null) return;
        pressed = null;
        e.Handled = true;
        Open();
    }

    /// <summary>Two fingers to the right send it away, onto the clipboard as a time out would.</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // ponytail: the sign of a rightward swipe is read from the docs, not from a real
        // touchpad; flip it if a swipe right on Windows does nothing.
        swipe = Math.Max(0, swipe - e.Delta.X);
        if (swipe >= 1) Dismiss(copying: true);
    }

    // Placing

    private void SlideIn()
    {
        var screen = (on is { } at ? Screens.ScreenFromPoint(at) : null) ?? Screens.Primary;
        if (screen is null) return;
        var resting = ThumbnailGeometry.Resting(screen.WorkingArea, screen.Scaling, size);
        var shadow = (int)(PinGeometry.Shadow * screen.Scaling);
        var end = new PixelPoint(resting.X - shadow, resting.Y - shadow);
        Slide(new PixelPoint(screen.WorkingArea.Right + (int)(20 * screen.Scaling), end.Y), end, () => { });
    }

    /// <summary>Moves the window from <paramref name="start"/> to <paramref name="end"/> over a
    /// quarter of a second, easing out, then calls <paramref name="done"/>.</summary>
    private void Slide(PixelPoint start, PixelPoint end, Action done)
    {
        if (!animate)
        {
            Position = end;
            done();
            return;
        }
        Position = start;
        var began = DateTime.UtcNow;
        var steps = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        steps.Tick += (_, _) =>
        {
            var t = Math.Min(1, (DateTime.UtcNow - began).TotalSeconds / 0.25);
            var eased = 1 - Math.Pow(1 - t, 3);
            Position = new PixelPoint((int)(start.X + (end.X - start.X) * eased), (int)(start.Y + (end.Y - start.Y) * eased));
            if (t < 1) return;
            steps.Stop();
            done();
        };
        steps.Start();
    }
}

/// <summary>The thumbnail's size and resting place.</summary>
internal static class ThumbnailGeometry
{
    private const double Largest = 240;
    private const double Inset = 20;

    /// <summary>The capture's point size, shrunk to no more than 240 points either way.</summary>
    public static Size Fit(Size points)
    {
        var fit = Math.Min(1, Math.Min(Largest / points.Width, Largest / points.Height));
        return new Size(Math.Max(1, points.Width * fit), Math.Max(1, points.Height * fit));
    }

    /// <summary>The top left of a thumbnail of <paramref name="size"/> points, 20 points in from
    /// the bottom right of <paramref name="workArea"/>, in physical pixels.</summary>
    public static PixelPoint Resting(PixelRect workArea, double scaling, Size size) =>
        new((int)Math.Round(workArea.Right - (Inset + size.Width) * scaling),
            (int)Math.Round(workArea.Bottom - (Inset + size.Height) * scaling));
}
