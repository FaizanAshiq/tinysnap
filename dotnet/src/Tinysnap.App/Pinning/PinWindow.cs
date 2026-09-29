using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using Point = Tinysnap.Core.Point;
using Rect = Tinysnap.Core.Rect;
using Size = Tinysnap.Core.Size;

namespace Tinysnap.App.Pinning;

/// <summary>One capture floating above every app. It opens without taking the focus, so pinning
/// a reference leaves the app being worked in at the front; clicking it makes it the active
/// window, which a window needs on Windows to take Esc, the digits and Ctrl+C.</summary>
internal sealed class PinWindow : Window
{
    private readonly SharedImage image;
    private readonly double scale;
    /// <summary>Drawn at a size the capture was given, which copy and save keep rather than
    /// taking the Export setting's.</summary>
    private readonly bool keepsSize;
    private readonly EditorServices services;
    private readonly PixelPoint? pointer;
    private readonly Border frame;

    /// <summary>A copy of the pin's image and its scale, for an editor. The copy is the
    /// receiver's: the pin disposes its own when it closes.</summary>
    public event Action<SKImage, double>? OpenRequested;

    /// <summary>The image's size on screen, in DIPs.</summary>
    internal Size ImageSize => new(frame.Width, frame.Height);

    internal double PinOpacity => frame.Opacity;

    internal bool HasShadow => frame.BoxShadow.Count > 0;

    /// <param name="pointer">Where the pointer is, in physical pixels: the pin opens centred on
    /// that monitor.</param>
    public PinWindow(SKImage image, double scale, bool keepsSize, EditorServices services, PixelPoint? pointer = null)
    {
        this.image = new SharedImage(image);
        this.scale = scale;
        this.keepsSize = keepsSize;
        this.services = services;
        this.pointer = pointer;
        Title = "Pinned capture";
        WindowDecorations = WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        frame = new Border
        {
            Width = image.Width / scale,
            Height = image.Height / scale,
            // ponytail: the shadow is drawn inside the window, so its margin takes clicks that a
            // system shadow would pass through; a DWM shadow if that ever gets in the way.
            Margin = new Thickness(PinGeometry.Shadow),
            BoxShadow = Shadow,
            Child = new FilledImage(this.image),
        };
        AutomationProperties.SetName(frame, "Pinned capture");
        AutomationProperties.SetHelpText(frame, "Drag to move, scroll to resize, 1 to 9 for opacity, Esc to close");
        frame.ContextMenu = Menu();
        Content = frame;
        Opened += (_, _) => Place();
        Closed += (_, _) => this.image.Release();
    }

    private static BoxShadows Shadow => new(new BoxShadow { OffsetY = 4, Blur = 16, Color = Color.FromArgb(90, 0, 0, 0) });

    /// <summary>At the capture's point size, no larger than 80% of the monitor with the pointer,
    /// centred on it.</summary>
    private void Place()
    {
        var screen = (pointer is { } at ? Screens.ScreenFromPoint(at) : null) ?? Screens.Primary;
        if (screen is null) return;
        var work = screen.WorkingArea;
        var scaling = screen.Scaling;
        var size = PinGeometry.Initial(ImageSize, new Size(work.Width / scaling, work.Height / scaling));
        (frame.Width, frame.Height) = (size.Width, size.Height);
        var outer = new PixelSize((int)((size.Width + PinGeometry.Shadow * 2) * scaling), (int)((size.Height + PinGeometry.Shadow * 2) * scaling));
        Position = new PixelPoint(work.X + (work.Width - outer.Width) / 2, work.Y + (work.Height - outer.Height) / 2);
    }

    private ContextMenu Menu()
    {
        var menu = new ContextMenu();
        foreach (var (title, action) in new (string, Action)[]
                 {
                     ("Copy", CopyImage), ("Save", SaveImage), ("Open in Editor", OpenInEditor), ("Close", Close),
                 })
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        return menu;
    }

    // Actions

    /// <summary>1 to 9 for 10% to 90%, 0 for solid. A see-through pin has no shadow, so it lines
    /// up cleanly with whatever it is compared against.</summary>
    private void SetOpacity(int tenths)
    {
        frame.Opacity = tenths == 0 ? 1 : tenths / 10.0;
        frame.BoxShadow = tenths == 0 ? Shadow : default;
    }

    private (ExportedImage Exported, byte[] Png)? Exported()
    {
        var scaleSetting = keepsSize ? ExportScale.Native : services.Preferences().ExportScale;
        if (Output.Export(new Document(new Capture(image.Image, scale)), scaleSetting) is { } output) return output;
        _ = services.Dialogs.Tell(this, "Tinysnap could not draw this pin.");
        return null;
    }

    private void CopyImage()
    {
        if (Exported() is not var (exported, png)) return;
        using var drawn = exported.Image;
        if (!Output.Copy(services.Clipboard, exported, png))
            _ = services.Dialogs.Tell(this, "Tinysnap could not copy to the clipboard. Another app may be holding it; try again.");
    }

    private void SaveImage()
    {
        if (Exported() is not var (exported, png)) return;
        exported.Image.Dispose();
        try
        {
            Output.Save(png, services.Preferences().SaveFolderPath, DateTimeOffset.Now);
        }
        catch (OutputException error)
        {
            _ = services.Dialogs.Tell(this, error.Message);
        }
    }

    private void OpenInEditor()
    {
        using var pixels = image.Image.PeekPixels();
        var copy = pixels is not null ? SKImage.FromPixelCopy(pixels) : image.Image.ToRasterImage(true);
        OpenRequested?.Invoke(copy, scale);
    }

    // Input

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (e.Key == Key.Escape || command && e.Key == Key.W)
            Close();
        else if (command && e.Key == Key.C)
            CopyImage();
        else if (command && e.Key == Key.S)
            SaveImage();
        else if (e.KeyModifiers == KeyModifiers.None && Digit(e.Key) is { } digit)
            SetOpacity(digit);
        else
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
    }

    private static int? Digit(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => null,
    };

    /// <summary>Double-click opens an editor; a single press drags the pin.</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (e.ClickCount == 2)
            OpenInEditor();
        else
            BeginMoveDrag(e);
    }

    /// <summary>Resizes around the pointer, keeping the shape, between 64 points on the short
    /// side and the monitor.</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0) return;
        e.Handled = true;
        var at = e.GetPosition(frame);
        var screen = Screens.ScreenFromWindow(this);
        var scaling = screen?.Scaling ?? DesktopScaling;
        Size? room = screen is null ? null : new Size(screen.WorkingArea.Width / scaling, screen.WorkingArea.Height / scaling);
        var resized = PinGeometry.Resized(new Rect(0, 0, frame.Width, frame.Height), new Point(at.X, at.Y),
                                          PinGeometry.WheelFactor(e.Delta.Y), room);
        (frame.Width, frame.Height) = (resized.Width, resized.Height);
        Position = new PixelPoint(Position.X + (int)Math.Round(resized.X * scaling), Position.Y + (int)Math.Round(resized.Y * scaling));
    }
}

/// <summary>Where a pin opens and how the wheel resizes it, in DIPs.</summary>
internal static class PinGeometry
{
    /// <summary>Room round the image for its shadow.</summary>
    public const double Shadow = 16;

    private const double ShortestSide = 64;

    /// <summary>At its point size, shrunk to 80% of the work area when it does not fit.</summary>
    public static Size Initial(Size image, Size workArea)
    {
        var fit = Math.Min(1, Math.Min(workArea.Width * 0.8 / image.Width, workArea.Height * 0.8 / image.Height));
        return new Size(image.Width * fit, image.Height * fit);
    }

    /// <summary>A wheel moves 10% a notch whatever delta it reports, which grows with its
    /// acceleration; a trackpad's fractions follow the fingers.</summary>
    public static double WheelFactor(double delta) =>
        Math.Abs(delta) >= 1 ? delta > 0 ? 1.1 : 1 / 1.1 : Math.Pow(1.1, delta);

    /// <summary><paramref name="frame"/> scaled by <paramref name="factor"/> about
    /// <paramref name="pointer"/>, keeping its shape, no larger than <paramref name="screen"/>
    /// and no smaller than 64 on the short side.</summary>
    public static Rect Resized(Rect frame, Point pointer, double factor, Size? screen)
    {
        var aspect = frame.Height / frame.Width;
        var width = frame.Width * factor;
        if (screen is { } room) width = Math.Min(width, Math.Min(room.Width, room.Height / aspect));
        width = Math.Max(width, ShortestSide / Math.Min(1, aspect));
        var height = width * aspect;
        var across = (pointer.X - frame.X) / frame.Width;
        var down = (pointer.Y - frame.Y) / frame.Height;
        return new Rect(pointer.X - across * width, pointer.Y - down * height, width, height);
    }
}
