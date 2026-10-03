using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;
using Tinysnap.App.Capturing;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using Point = Avalonia.Point;
using Style = Avalonia.Styling.Style;

namespace Tinysnap.App.Library;

/// <summary>Every kept capture, newest first, grouped by day. Enter or double-click edits, Space
/// previews, Ctrl+C copies, Ctrl+S saves, Ctrl+P pins, dragging drops the image anywhere, Delete
/// moves it to the Recycle Bin; the toolbar, each tile's hover buttons and the right-click menu do
/// the same without a key.</summary>
internal sealed class LibraryWindow : Window
{
    private readonly CaptureController captures;
    private readonly StackPanel daysPanel = new();
    private readonly TextBlock emptyNote = new()
    {
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        MaxWidth = 360,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush"),
    };
    private readonly Button copyButton, saveButton;
    /// <summary>Small copies of each image, kept until the image changes.</summary>
    private readonly Dictionary<string, (DateTime Modified, Bitmap? Picture, PixelSize Pixels)> thumbnails = [];
    private readonly List<LibraryTile> tiles = [];
    private readonly List<TileGrid> grids = [];
    private LibraryEntry? selected;
    private PointerPressedEventArgs? pressed;
    private Point pressedAt;

    internal IReadOnlyList<(string Title, IReadOnlyList<LibraryEntry> Entries)> Days { get; private set; } = [];

    /// <summary>Copy, Save, Edit, Pin and Move to Recycle Bin, each for the selected capture.</summary>
    internal IReadOnlyList<Button> ToolbarButtons { get; }

    internal PreviewWindow? Preview { get; private set; }

    public LibraryWindow(CaptureController captures)
    {
        this.captures = captures;
        Title = "Library";
        Width = 1000;
        Height = 700;
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Styles.AddRange(TileActionStyles());

        copyButton = ToolbarButton(ToolIcons.Copy, "Copy", "Copy (Ctrl+C)", () => CopySelected(copyButton!));
        saveButton = ToolbarButton(ToolIcons.Save, "Save", "Save to the save folder (Ctrl+S)", () => SaveSelected(saveButton!));
        ToolbarButtons =
        [
            copyButton,
            saveButton,
            ToolbarButton(ToolIcons.For(Tool.Freehand), "Edit", "Edit (Enter)", EditSelected),
            ToolbarButton(ToolIcons.Pin, "Pin", "Pin on top of every window (Ctrl+P)", PinSelected),
            ToolbarButton(ToolIcons.Delete, $"Move to {SystemWords.Bin}", $"Move to {SystemWords.Bin} (Delete)", TrashSelected),
        ];
        var toolbar = new Border
        {
            Height = 40,
            Padding = new Thickness(8, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center },
            [!BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush"),
        };
        foreach (var button in ToolbarButtons) ((StackPanel)toolbar.Child).Children.Add(button);
        DockPanel.SetDock(toolbar, Dock.Top);

        var scroll = new ScrollViewer
        {
            Content = daysPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 0, 24),
        };
        var strip = new WindowTabStrip(captures.Tabs, this);
        DockPanel.SetDock(strip, Dock.Top);
        Content = new DockPanel { Children = { strip, toolbar, new Panel { Children = { scroll, emptyNote } } } };

        captures.LibraryChanged += Reload;
        Closed += (_, _) =>
        {
            captures.LibraryChanged -= Reload;
            Preview?.Close();
            foreach (var (_, picture, _) in thumbnails.Values) picture?.Dispose();
        };
        Refresh();
    }

    /// <summary>Reloaded, shown, and brought in front.</summary>
    public void ShowInFront()
    {
        Reload();
        if (!IsVisible) Show();
        Activate();
    }

    internal LibraryTile? TileFor(LibraryEntry entry) => tiles.FirstOrDefault(tile => tile.Entry == entry);

    internal LibraryEntry? Selected
    {
        get => selected;
        set
        {
            selected = value;
            Refresh();
            if (value is not null) TileFor(value)?.BringIntoView();
            if (Preview is not null && value is not null) Preview.Show(value);
        }
    }

    // Contents

    /// <summary>Also has any image left older than its edits, by a crash or a quit mid render,
    /// drawn again, so what the grid and the preview show matches what reopening gives. The old
    /// image shows until the new one lands and the grid reloads. The selection is kept.</summary>
    private void Reload()
    {
        var open = captures.OpenEntryNames;
        var entries = captures.Library.Entries();
        foreach (var entry in entries.Where(e => !open.Contains(e.Name) && captures.Library.ImageIsStale(e)))
            if (captures.Library.Open(entry) is { IsEditable: true } opened) captures.Render(entry, opened.Document);

        var today = DateTime.Today;
        Days = entries
            .GroupBy(entry => entry.Captured.ToLocalTime().Date)
            .Select(day => (DayTitle(day.Key, today), (IReadOnlyList<LibraryEntry>)day.ToList()))
            .ToList();
        if (selected is not null && !entries.Contains(selected)) selected = null;

        daysPanel.Children.Clear();
        tiles.Clear();
        grids.Clear();
        foreach (var (title, dayEntries) in Days)
        {
            daysPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(LibraryLayout.Edge, 20, LibraryLayout.Edge, 10),
            });
            var grid = new TileGrid();
            foreach (var entry in dayEntries) grid.Children.Add(Tile(entry));
            grids.Add(grid);
            daysPanel.Children.Add(grid);
        }
        emptyNote.IsVisible = entries.Count == 0;
        emptyNote.Text = captures.Services.Preferences().KeepLibrary
            ? "Captures appear here and stay for 30 days."
            : "The library is off. Turn it on in Settings to keep captures here for 30 days.";
        Refresh();
    }

    private static string DayTitle(DateTime day, DateTime today) =>
        day == today ? "Today"
        : day == today.AddDays(-1) ? "Yesterday"
        : day.ToString("D", CultureInfo.CurrentCulture);

    private LibraryTile Tile(LibraryEntry entry)
    {
        var (picture, pixels) = Thumbnail(entry);
        var tile = new LibraryTile(entry, picture, pixels) { ContextMenu = Menu(entry) };
        tile.CopyRequested += button => Copy(entry, button);
        tile.SaveRequested += button => Save(entry, button);
        tile.EditRequested += () => captures.Open(entry);
        tile.PointerPressed += (_, e) => Press(tile, e);
        tile.PointerMoved += (_, e) => _ = Drag(tile, e);
        tile.PointerReleased += (_, _) => pressed = null;
        tiles.Add(tile);
        return tile;
    }

    /// <summary>The rendered image at no more than 640 pixels across, and its full size.</summary>
    private (Bitmap? Picture, PixelSize Pixels) Thumbnail(LibraryEntry entry)
    {
        var path = File.Exists(entry.ImagePath) ? entry.ImagePath : entry.OriginalPath;
        var modified = File.GetLastWriteTimeUtc(path);
        if (thumbnails.TryGetValue(path, out var cached) && cached.Modified == modified) return (cached.Picture, cached.Pixels);
        try
        {
            using var codec = SKCodec.Create(path);
            var pixels = codec is null ? default : new PixelSize(codec.Info.Width, codec.Info.Height);
            using var stream = File.OpenRead(path);
            var picture = Bitmap.DecodeToWidth(stream, Math.Min(640, Math.Max(1, pixels.Width)));
            // ponytail: an image replaced while its old thumbnail still shows is left to the
            // collector rather than disposed under a tile that may be drawing it.
            thumbnails[path] = (modified, picture, pixels);
            return (picture, pixels);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return (null, default);
        }
    }

    private ContextMenu Menu(LibraryEntry entry)
    {
        var menu = new ContextMenu();
        foreach (var (title, action) in new (string, Action)[]
                 {
                     ("Edit", () => captures.Open(entry)),
                     ("Copy", () => Copy(entry, null)),
                     ("Save", () => Save(entry, null)),
                     ("Pin", () => Pin(entry)),
                     ($"Show in {SystemWords.FileManager}", () => Reveal(entry)),
                     ($"Move to {SystemWords.Bin}", () => Trash(entry)),
                 })
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        // Right-click selects what is under the pointer first, as Explorer does.
        menu.Opening += (_, _) => Selected = entry;
        return menu;
    }

    /// <summary>The selection shown on the tiles and the toolbar enabled to match.</summary>
    private void Refresh()
    {
        foreach (var tile in tiles) tile.IsSelected = tile.Entry == selected;
        foreach (var button in ToolbarButtons) button.IsEnabled = selected is not null;
    }

    // Actions

    private void CopySelected(Button button)
    {
        if (selected is { } entry) Copy(entry, button);
    }

    private void SaveSelected(Button button)
    {
        if (selected is { } entry) Save(entry, button);
    }

    private void EditSelected()
    {
        if (selected is { } entry) captures.Open(entry);
    }

    private void PinSelected()
    {
        if (selected is { } entry) Pin(entry);
    }

    private void TrashSelected()
    {
        if (selected is { } entry) Trash(entry);
    }

    /// <summary>The entry drawn from its edits, so a capture with a size of its own comes out at
    /// it and one without takes the Export setting.</summary>
    private (ExportedImage Exported, byte[] Png)? Exported(LibraryEntry entry)
    {
        captures.Flush(entry);
        if (captures.Library.Open(entry)?.Document is { } document
            && Output.Export(document, captures.Services.Preferences().ExportScale) is { } output)
            return output;
        _ = captures.Services.Dialogs.Tell(this, "Tinysnap could not draw this capture.");
        return null;
    }

    private void Copy(LibraryEntry entry, Button? button)
    {
        if (Exported(entry) is not var (exported, png)) return;
        using var image = exported.Image;
        if (!Output.Copy(captures.Services.Clipboard, exported, png))
        {
            _ = captures.Services.Dialogs.Tell(this, "Tinysnap could not copy to the clipboard. Another app may be holding it; try again.");
            return;
        }
        if (button is not null) Tick(button);
    }

    /// <summary>Into the save folder, as the editor's Save does.</summary>
    private void Save(LibraryEntry entry, Button? button)
    {
        if (Exported(entry) is not var (exported, png)) return;
        exported.Image.Dispose();
        try
        {
            Output.Save(png, captures.Services.Preferences().SaveFolderPath, DateTimeOffset.Now);
        }
        catch (OutputException error)
        {
            _ = captures.Services.Dialogs.Tell(this, error.Message);
            return;
        }
        if (button is not null) Tick(button);
    }

    private void Pin(LibraryEntry entry)
    {
        if (!captures.PinEntry(entry)) _ = captures.Services.Dialogs.Tell(this, "Tinysnap could not draw this capture.");
    }

    private void Reveal(LibraryEntry entry)
    {
        captures.Flush(entry);
        captures.Files.Reveal(File.Exists(entry.ImagePath) ? entry.ImagePath : entry.Folder);
    }

    /// <summary>To the Recycle Bin rather than gone, so a slip of the Delete key can be undone.</summary>
    private void Trash(LibraryEntry entry)
    {
        if (captures.OpenEntryNames.Contains(entry.Name))
        {
            _ = captures.Services.Dialogs.Tell(this, $"This capture is open in an editor. Close its window, then move it to the {SystemWords.Bin}.");
            return;
        }
        if (!captures.Files.MoveToRecycleBin(entry.Folder))
        {
            _ = captures.Services.Dialogs.Tell(this, $"Tinysnap could not move this capture to the {SystemWords.Bin}.");
            return;
        }
        captures.NotifyLibraryChanged();
    }

    /// <summary>A tick on the button that asked, for a moment: copying and saving change nothing
    /// on screen, so without it a click looks as if it did nothing.</summary>
    private static void Tick(Button button)
    {
        var content = button.Content;
        button.Content = Glyphs.Icon(ToolIcons.Done, 16);
        DispatcherTimer.RunOnce(() => button.Content = content, TimeSpan.FromSeconds(1));
    }

    private void TogglePreview()
    {
        if (Preview is not null)
        {
            Preview.Close();
            return;
        }
        if (selected is not { } entry) return;
        var preview = new PreviewWindow();
        preview.StepRequested += Step;
        preview.Closed += (_, _) =>
        {
            if (Preview == preview) Preview = null;
        };
        Preview = preview;
        preview.Show(entry);
        preview.Show(this);
    }

    // Keys and pointer

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        switch (e.Key)
        {
            case Key.C when command: CopySelected(copyButton); break;
            case Key.S when command: SaveSelected(saveButton); break;
            case Key.P when command: PinSelected(); break;
            case Key.Enter: EditSelected(); break;
            case Key.Delete or Key.Back: TrashSelected(); break;
            case Key.Space: TogglePreview(); break;
            case Key.Left or Key.Right or Key.Up or Key.Down: Step(e.Key); break;
            default:
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    /// <summary>The arrows move the selection through the tiles in reading order, a row at a time
    /// for up and down.</summary>
    private void Step(Key key)
    {
        var all = Days.SelectMany(day => day.Entries).ToList();
        if (all.Count == 0) return;
        var row = grids.FirstOrDefault()?.ColumnCount ?? 1;
        var at = selected is null ? -1 : all.IndexOf(selected);
        var next = key switch
        {
            Key.Left => at - 1,
            Key.Right => at + 1,
            Key.Up => at - row,
            _ => at < 0 ? 0 : at + row,
        };
        Selected = all[Math.Clamp(next, 0, all.Count - 1)];
        TileFor(selected!)?.Focus();
    }

    private void Press(LibraryTile tile, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(tile);
        if (point.Properties.IsRightButtonPressed)
        {
            Selected = tile.Entry;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        Selected = tile.Entry;
        if (e.ClickCount == 2)
        {
            pressed = null;
            captures.Open(tile.Entry);
            return;
        }
        pressed = e;
        pressedAt = e.GetPosition(this);
    }

    /// <summary>A few points of movement drags the rendered PNG itself, which Explorer, mail and
    /// chat apps and browsers all take.</summary>
    private async Task Drag(LibraryTile tile, PointerEventArgs e)
    {
        if (pressed is not { } start) return;
        var at = e.GetPosition(this);
        if (Math.Sqrt(Math.Pow(at.X - pressedAt.X, 2) + Math.Pow(at.Y - pressedAt.Y, 2)) <= 4) return;
        pressed = null;
        captures.Flush(tile.Entry);
        if (await StorageProvider.TryGetFileFromPathAsync(tile.Entry.ImagePath) is not { } file) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(file));
        await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Copy);
    }

    // Look

    private static Button ToolbarButton(string icon, string name, string tip, Action action)
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

    /// <summary>White labels on a translucent ground over the fade, Copy on the accent, and dark
    /// on white under the pointer.</summary>
    private static IEnumerable<IStyle> TileActionStyles()
    {
        static Selector Presenter(Selector button) => button.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
        yield return new Style(x => x.OfType<Button>().Class("tile-action"))
        {
            Setters =
            {
                new Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(9, 3)),
                new Setter(Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty, 12.0),
                new Setter(Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
            },
        };
        yield return new Style(x => Presenter(x.OfType<Button>().Class("tile-action")))
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.FromArgb(56, 255, 255, 255))),
                new Setter(ContentPresenter.ForegroundProperty, Brushes.White),
            },
        };
        yield return new Style(x => Presenter(x.OfType<Button>().Class("tile-action").Class("primary")))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, new SolidColorBrush(Accent.Color)) },
        };
        yield return new Style(x => Presenter(x.OfType<Button>().Class("tile-action").Class(":pointerover")))
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, Brushes.White),
                new Setter(ContentPresenter.ForegroundProperty, new SolidColorBrush(Color.FromRgb(28, 28, 30))),
            },
        };
    }
}
