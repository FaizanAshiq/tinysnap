using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Tinysnap.Core;
using CoreSize = Tinysnap.Core.Size;
using Rect = Avalonia.Rect;

namespace Tinysnap.App.Editing;

/// <summary>One window per capture: the toolbar, the canvas on a checkerboard, and the style bar
/// over the canvas's top right corner.</summary>
internal sealed class EditorWindow : Window
{
    private const double ToolbarHeight = 40;

    /// <summary>The toolbar's groups, by what the tools do: pick and frame, draw, label, focus,
    /// redact. Each group is set apart by a divider.</summary>
    private static readonly Tool[][] Groups =
    [
        [Tool.Select, Tool.Crop],
        [Tool.Arrow, Tool.Line, Tool.Rectangle, Tool.Oval, Tool.Freehand, Tool.Highlighter],
        [Tool.Text, Tool.Step, Tool.Image],
        [Tool.Spotlight, Tool.Magnifier, Tool.Measure],
        [Tool.Blur, Tool.Pixelate, Tool.Erase],
    ];

    private static readonly List<EditorWindow> Open = [];

    private readonly ScrollViewer scroll;
    private readonly Border colorWell = new()
    {
        Width = 14,
        Height = 14,
        CornerRadius = new CornerRadius(3),
        BorderThickness = new Thickness(1),
        [!BorderBrushProperty] = new DynamicResourceExtension("SystemControlForegroundBaseLowBrush"),
    };
    private readonly TextBlock colorLabel = new()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        MinWidth = 56,
    };
    private readonly PixelRect? around;
    private readonly EditorServices services;
    private string? pointerColor;
    /// <summary>Set once the capture may close without asking: saved or discarded in the prompt,
    /// or pinned.</summary>
    private bool closingForGood;

    public CanvasControl Canvas { get; }
    public StyleBar StyleBar { get; }
    public Control Toolbar { get; }
    internal IReadOnlyList<(Tool Tool, ToggleButton Button)> ToolButtons { get; }
    internal int GroupDividers { get; }

    /// <summary>Copy, Save, the drag-out handle and Pin, at the start of the toolbar.</summary>
    internal IReadOnlyList<Control> OutputButtons { get; }

    private readonly Button copyButton;
    private readonly Button saveButton;

    /// <param name="around">Where the capture was taken, in physical pixels, so the editor opens
    /// on that monitor.</param>
    public EditorWindow(EditorSession session, DateTimeOffset captured, EditorServices services, PixelRect? around = null)
    {
        this.around = around;
        this.services = services;
        Title = TitleFor(captured, DateTimeOffset.Now, TimeZoneInfo.Local, CultureInfo.CurrentCulture);
        MinHeight = 280;

        Canvas = new CanvasControl(session)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(EditorFit.Margin),
        };
        scroll = new ScrollViewer
        {
            Content = Canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        StyleBar = new StyleBar(Canvas)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12),
        };

        var buttons = new List<(Tool, ToggleButton)>();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copyButton = OutputButton(ToolIcons.Copy, "Copy", "Copy (Ctrl+C)", CopyImage);
        saveButton = OutputButton(ToolIcons.Save, "Save", "Save to the save folder (Ctrl+S); Ctrl+Shift+S asks where", SaveImage);
        var dragHandle = DragHandle();
        var pin = OutputButton(ToolIcons.Pin, "Pin and close", "Pin and close (Ctrl+P)", PinImage);
        OutputButtons = [copyButton, saveButton, dragHandle, pin];
        foreach (var control in OutputButtons) bar.Children.Add(control);
        bar.Children.Add(Divider());
        for (var group = 0; group < Groups.Length; group++)
        {
            if (group > 0) bar.Children.Add(Divider());
            foreach (var tool in Groups[group])
            {
                var button = new ToggleButton
                {
                    Content = Glyphs.Icon(ToolIcons.For(tool)),
                    Width = 34,
                    Height = 30,
                    Padding = new Thickness(0),
                    // Bare icons; only the chosen tool is lit.
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                };
                ToolTip.SetTip(button, Tooltip(tool));
                AutomationProperties.SetName(button, tool.Title());
                button.Click += (_, _) =>
                {
                    Canvas.Choose(tool);
                    Canvas.Focus();
                };
                buttons.Add((tool, button));
                bar.Children.Add(button);
            }
        }
        GroupDividers = Groups.Length - 1;
        ToolButtons = buttons;
        var readout = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { colorWell, colorLabel },
        };
        ToolTip.SetTip(readout, "Colour under the pointer; Tab copies it");
        bar.Children.Add(readout);
        Toolbar = new Border
        {
            Child = bar,
            Height = ToolbarHeight,
            Padding = new Thickness(8, 0),
            [!BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush"),
        };
        DockPanel.SetDock(Toolbar, Dock.Top);

        var page = new Grid { Children = { scroll, StyleBar } };
        Content = new DockPanel { Children = { Toolbar, page } };
        PaintGround();
        ActualThemeVariantChanged += (_, _) => PaintGround();

        Canvas.Changed += Refresh;
        Canvas.ZoomRequested += notches => ZoomTo(notches > 0 ? EditorFit.ZoomIn(Canvas.Zoom) : EditorFit.ZoomOut(Canvas.Zoom));
        Canvas.CloseRequested += Close;
        Canvas.PointerColor += ShowColor;
        Canvas.CopyColorRequested += CopyColor;
        Canvas.ImagePickRequested += () => _ = PickImage();
        Opened += (_, _) =>
        {
            Toolbar.Measure(Avalonia.Size.Infinity);
            MinWidth = Toolbar.DesiredSize.Width;
            Place();
            Canvas.Focus();
        };
        Closed += (_, _) => Open.Remove(this);
        Open.Add(this);
        Refresh();
    }

    /// <summary>"Capture at 9:41:12 AM", with the date as well when it was not today.</summary>
    public static string TitleFor(DateTimeOffset captured, DateTimeOffset now, TimeZoneInfo zone, CultureInfo culture)
    {
        var local = TimeZoneInfo.ConvertTime(captured, zone);
        var today = local.Date == TimeZoneInfo.ConvertTime(now, zone).Date;
        var day = today ? "" : local.ToString("d", culture) + " ";
        return $"Capture at {day}{local.ToString("T", culture)}";
    }

    private static Button OutputButton(string icon, string name, string tip, Action action)
    {
        var button = new Button
        {
            Content = Glyphs.Icon(icon),
            Width = 34,
            Height = 30,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    private static string Tooltip(Tool tool)
    {
        var shortcut = $"{tool.Title()} ({char.ToUpperInvariant(tool.Key())})";
        return tool == Tool.Select ? shortcut + ", or hold Ctrl with any tool" : shortcut;
    }

    private static Control Divider() => new Border
    {
        Width = 1,
        Height = 20,
        Margin = new Thickness(6, 0),
        [!BackgroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseLowBrush"),
    };

    private void Refresh()
    {
        foreach (var (tool, button) in ToolButtons) button.IsChecked = tool == Canvas.Session.Tool;
    }

    // Ground

    /// <summary>Outside the canvas the window shows a checkerboard, as image editors do, so where
    /// the image ends is plain whatever colour the capture is.</summary>
    private void PaintGround()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var base_ = new SolidColorBrush(dark ? Color.FromRgb(41, 41, 41) : Color.FromRgb(230, 230, 230));
        var square = new SolidColorBrush(dark ? Color.FromRgb(56, 56, 56) : Color.FromRgb(247, 247, 247));
        scroll.Background = new DrawingBrush
        {
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing { Brush = base_, Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)) },
                    new GeometryDrawing { Brush = square, Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)) },
                    new GeometryDrawing { Brush = square, Geometry = new RectangleGeometry(new Rect(8, 8, 8, 8)) },
                },
            },
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
            Stretch = Stretch.None,
        };
    }

    // Placing and zooming

    private CoreSize CanvasAtFullSize => new(Canvas.Mapping.Size.Width / Canvas.Zoom, Canvas.Mapping.Size.Height / Canvas.Zoom);

    /// <summary>At 100% when it fits, shrunk to fit the monitor it was captured on when it does
    /// not, centred there, and a step down and right of the last editor on the same monitor.</summary>
    private void Place()
    {
        var screen = (around is { } area ? Screens.ScreenFromPoint(area.Center) : null) ?? Screens.Primary;
        var scaling = screen?.Scaling ?? 1;
        var work = screen?.WorkingArea;
        var workDips = work is { } w ? new CoreSize(w.Width / scaling, w.Height / scaling) : new CoreSize(1920, 1040);
        var (client, zoom) = EditorFit.Initial(CanvasAtFullSize, workDips, ToolbarHeight, new CoreSize(MinWidth, MinHeight));
        Canvas.Zoom = zoom;
        Width = client.Width;
        Height = client.Height;
        if (work is not { } workArea) return;
        var size = new PixelSize((int)(client.Width * scaling), (int)(client.Height * scaling));
        var position = new PixelPoint(workArea.X + (workArea.Width - size.Width) / 2, workArea.Y + (workArea.Height - size.Height) / 2);
        if (Open.LastOrDefault(e => e != this && e.IsVisible && Screens.ScreenFromWindow(e) == screen) is { } previous)
        {
            var step = (int)(28 * scaling);
            position = new PixelPoint(previous.Position.X + step, previous.Position.Y + step);
            if (position.X + size.Width > workArea.Right || position.Y + size.Height > workArea.Bottom)
                position = new PixelPoint(workArea.X + step, workArea.Y + step);
        }
        Position = position;
    }

    private void ZoomTo(double zoom) => Canvas.Zoom = EditorFit.Clamp(zoom);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            switch (e.Key)
            {
                case Key.C when !shift:
                    CopyImage();
                    e.Handled = true;
                    return;
                case Key.S when shift:
                    _ = SaveImageAs();
                    e.Handled = true;
                    return;
                case Key.S:
                    SaveImage();
                    e.Handled = true;
                    return;
                case Key.P:
                    PinImage();
                    e.Handled = true;
                    return;
                case Key.W:
                    Close();
                    e.Handled = true;
                    return;
                case Key.OemPlus or Key.Add:
                    ZoomTo(EditorFit.ZoomIn(Canvas.Zoom));
                    e.Handled = true;
                    return;
                case Key.OemMinus or Key.Subtract:
                    ZoomTo(EditorFit.ZoomOut(Canvas.Zoom));
                    e.Handled = true;
                    return;
                case Key.D0 or Key.NumPad0:
                    var view = scroll.Viewport;
                    ZoomTo(EditorFit.Fit(CanvasAtFullSize, new CoreSize(view.Width, view.Height)));
                    e.Handled = true;
                    return;
                case Key.D1 or Key.NumPad1:
                    ZoomTo(1);
                    e.Handled = true;
                    return;
            }
        }
        base.OnKeyDown(e);
    }

    // Colour readout

    private void ShowColor(string hex)
    {
        pointerColor = hex;
        colorLabel.Text = hex[1..];
        colorWell.Background = new SolidColorBrush(Color.Parse(hex));
    }

    /// <summary>Tab copies the hex under the pointer, from any tool.</summary>
    private void CopyColor()
    {
        if (pointerColor is not { } hex || !services.Clipboard.SetText(hex)) return;
        colorLabel.Text = "Copied";
        DispatcherTimer.RunOnce(() =>
        {
            if (pointerColor is { } shownHex) colorLabel.Text = shownHex[1..];
        }, TimeSpan.FromSeconds(0.8));
    }

    // Output

    /// <summary>The capture as it stands, text being typed included, flattened for output. Null,
    /// after telling the person, when it could not be drawn.</summary>
    private (ExportedImage Exported, byte[] Png)? Finished(ExportScale scale)
    {
        Canvas.Session.FinishTyping();
        Canvas.SessionChanged();
        if (Output.Export(Canvas.Session.Display, scale) is { } output) return output;
        _ = services.Dialogs.Tell(this, "Tinysnap could not draw this capture.");
        return null;
    }

    /// <summary>Copies at the Export setting's scale; the editor stays, and a tick says it worked.</summary>
    private void CopyImage()
    {
        if (Finished(services.Preferences().ExportScale) is not var (exported, png)) return;
        using var image = exported.Image;
        if (!Output.Copy(services.Clipboard, exported, png))
        {
            _ = services.Dialogs.Tell(this, "Tinysnap could not copy to the clipboard. Another app may be holding it; try again.");
            return;
        }
        Canvas.Session.MarkSaved();
        Tick(copyButton, ToolIcons.Copy);
    }

    /// <summary>The PNG of the capture at its own size, the image drawn for it let go at once.</summary>
    private byte[]? FinishedPng()
    {
        if (Finished(ExportScale.Native) is not var (exported, png)) return null;
        exported.Image.Dispose();
        return png;
    }

    private void SaveImage() => SaveToFolder();

    /// <summary>Saves into the save folder under a name for now. False, after telling the person,
    /// when that failed.</summary>
    private bool SaveToFolder()
    {
        if (FinishedPng() is not { } png) return false;
        try
        {
            Output.Save(png, services.Preferences().SaveFolderPath, DateTimeOffset.Now);
        }
        catch (OutputException error)
        {
            _ = services.Dialogs.Tell(this, error.Message);
            return false;
        }
        Canvas.Session.MarkSaved();
        Tick(saveButton, ToolIcons.Save);
        return true;
    }

    /// <summary>Asks where, starting in the save folder.</summary>
    private async Task SaveImageAs()
    {
        if (FinishedPng() is not { } png) return;
        var now = DateTimeOffset.Now;
        var folder = services.Preferences().SaveFolderPath;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Capture",
            SuggestedFileName = FileNaming.FileName(now, name => File.Exists(Path.Combine(folder, name))),
            SuggestedStartLocation = Directory.Exists(folder) ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null,
            DefaultExtension = "png",
            FileTypeChoices = [FilePickerFileTypes.ImagePng],
            ShowOverwritePrompt = true,
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(png);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await services.Dialogs.Tell(this, $"Tinysnap could not save {file.Name}. {error.Message}");
            return;
        }
        Canvas.Session.MarkSaved();
        Tick(saveButton, ToolIcons.Save);
    }

    /// <summary>Opens a pin of the capture and closes the editor. A pin keeps the capture's own
    /// size when it has one.</summary>
    private void PinImage()
    {
        if (services.Pin is not { } pin || Finished(ExportScale.Native) is not var (exported, _)) return;
        pin(exported, Canvas.Session.Display.Resize is not null);
        Canvas.Session.MarkSaved();
        closingForGood = true;
        Close();
    }

    /// <summary>A button shows a tick for a moment after it worked.</summary>
    private static void Tick(Button button, string icon)
    {
        button.Content = Glyphs.Icon(ToolIcons.Done);
        DispatcherTimer.RunOnce(() => button.Content = Glyphs.Icon(icon), TimeSpan.FromSeconds(1.2));
    }

    /// <summary>A handle to drag the capture into another app as a PNG file.</summary>
    private Control DragHandle()
    {
        var handle = new Border
        {
            Child = Glyphs.Icon(ToolIcons.DragOut),
            Width = 34,
            Height = 30,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(handle, "Drag into another app");
        AutomationProperties.SetName(handle, "Drag out");
        handle.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            if (DragFile() is not { } path || await StorageProvider.TryGetFileFromPathAsync(path) is not { } file) return;
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(file));
            if (await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy) != DragDropEffects.None) Canvas.Session.MarkSaved();
        };
        return handle;
    }

    /// <summary>The capture written to a temporary PNG for dragging out, or null when it could not
    /// be written.</summary>
    internal string? DragFile()
    {
        if (FinishedPng() is not { } png) return null;
        return Output.TemporaryFile(png, DateTimeOffset.Now);
    }

    /// <summary>Unsaved edits are not lost without asking: Save saves and closes, Discard closes,
    /// Cancel keeps the editor.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (closingForGood || e.Cancel) return;
        Canvas.Session.FinishTyping();
        Canvas.SessionChanged();
        if (!Canvas.Session.IsUnsaved) return;
        var asking = services.Dialogs.AskToSave(this);
        // An answer already given decides this close; closing again from inside it would re-enter.
        if (asking.IsCompleted)
        {
            e.Cancel = !MayClose(asking.Result);
            return;
        }
        e.Cancel = true;
        if (!MayClose(await asking)) return;
        closingForGood = true;
        Close();
    }

    private bool MayClose(CloseChoice choice) => choice switch
    {
        CloseChoice.Discard => true,
        CloseChoice.Save => SaveToFolder(),
        _ => false,
    };

    // Images

    /// <summary>The image tool asks for a picture; cancelling leaves the tool out.</summary>
    private async Task PickImage()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an Image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) Canvas.InsertFile(path);
    }
}
