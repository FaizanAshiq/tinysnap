using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tinysnap.Core;
using Point = Avalonia.Point;

namespace Tinysnap.App.Editing;

/// <summary>The capture's shapes, top first, each with duplicate, eye and lock. Floats over the
/// canvas under the style bar, in its look, and is a list box, so the arrows and screen readers
/// work as in any list. A row dragged up or down moves its shape in the order.</summary>
internal sealed class LayersPanel : Border
{
    public const double PanelWidth = 244;
    public const double RowHeight = 32;

    /// <summary>One row, for the window and the tests.</summary>
    internal sealed record LayerRow(Guid Id, string Name, bool IsLocked, bool IsHidden, ListBoxItem Item,
                                    Button DuplicateButton, Button DeleteButton, Button HideButton, Button LockButton,
                                    TextBlock Label);

    private readonly record struct Shape(Guid Id, Tool Tool, string Name, bool IsLocked, bool IsHidden);

    /// <summary>A row was chosen, or none.</summary>
    public event Action<Guid?>? Selected;

    /// <summary>A row dragged to a place in the document's list, bottom first: its shape moves
    /// there at once, before it is let go.</summary>
    public event Action<Guid, int>? DraggedTo;

    /// <summary>The dragged row let go on the list, where it now stands.</summary>
    public event Action? Dropped;

    /// <summary>The dragged row let go anywhere else, or Esc: its shape goes back.</summary>
    public event Action? DragCancelled;

    public event Action<Guid, bool>? HideChanged;
    public event Action<Guid, bool>? LockChanged;
    public event Action<Guid>? DuplicateRequested;
    public event Action<Guid>? DeleteRequested;

    /// <summary>The row under the pointer, so the canvas can border its shape.</summary>
    public event Action<Guid?>? HoverChanged;

    /// <summary>A click on a row: the keys go back to the canvas.</summary>
    public event Action? Clicked;

    public event Action? CloseRequested;

    /// <summary>Ctrl+Z in the list, and Ctrl+Shift+Z or Ctrl+Y: undo and redo work wherever the
    /// keys are, as on the Mac, a row dragged included.</summary>
    public event Action? UndoRequested;
    public event Action? RedoRequested;

    internal ListBox List { get; }
    /// <summary>The row being dragged, lifted above the list under the pointer.</summary>
    internal Border DragGhost { get; } = new()
    {
        IsVisible = false,
        IsHitTestVisible = false,
        Height = RowHeight,
        Margin = new Thickness(4, 0),
        Padding = new Thickness(8, 0),
        CornerRadius = new CornerRadius(6),
        VerticalAlignment = VerticalAlignment.Top,
        BoxShadow = BoxShadows.Parse("0 4 12 0 #59000000"),
        [!BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundChromeMediumBrush"),
    };
    internal TextBlock EmptyNote { get; }
    internal IReadOnlyList<LayerRow> Rows => rows;

    private readonly TextBlock count = new() { FontSize = 12, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Right };
    private List<LayerRow> rows = [];
    private Shape[] shown = [];
    /// <summary>Set while the list is told the selection, so that is not sent back as a choice.</summary>
    private bool syncing;
    private (Guid Id, Point Start)? press;
    /// <summary>The row being dragged, faded where it stands while its card follows the pointer.</summary>
    private Guid? lifted;
    private IPointer? dragging;

    public LayersPanel()
    {
        Width = PanelWidth;
        Padding = new Thickness(6, 4, 6, 6);
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(0.5);
        this[!BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush");
        this[!BorderBrushProperty] = new DynamicResourceExtension("SystemControlForegroundBaseLowBrush");
        BoxShadow = BoxShadows.Parse("0 2 10 0 #40000000");

        var header = new Grid
        {
            Margin = new Thickness(8, 6, 8, 6),
            Children = { new TextBlock { Text = "Layers", FontSize = 13, FontWeight = FontWeight.SemiBold }, count },
        };
        EmptyNote = new TextBlock { Text = "Shapes you draw show here", FontSize = 13, Opacity = 0.6, Margin = new Thickness(8, 2, 8, 8) };
        List = new ListBox { SelectionMode = SelectionMode.Single, Background = Brushes.Transparent };
        AutomationProperties.SetName(List, "Layers");
        StyleChosenRows(List);
        List.SelectionChanged += (_, _) =>
        {
            if (syncing) return;
            var index = List.SelectedIndex;
            Selected?.Invoke(index >= 0 && index < rows.Count ? rows[index].Id : null);
        };
        // Before the row's own handler, which takes Space to choose the row.
        List.AddHandler(KeyDownEvent, OnListKey, RoutingStrategies.Tunnel);
        List.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        List.AddHandler(PointerMovedEvent, OnDragged, RoutingStrategies.Tunnel);
        List.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);

        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(EmptyNote, Dock.Top);
        Child = new DockPanel { Children = { header, EmptyNote, new Panel { Children = { List, DragGhost } } } };
    }

    /// <summary>The chosen row filled with the accent colour, its words and icons white, as the
    /// toolbar marks the chosen tool.</summary>
    private static void StyleChosenRows(ListBox list)
    {
        var accent = new DynamicResourceExtension("SystemControlHighlightAccentBrush");
        Func<Selector?, Selector>[] chosen =
        [
            x => x.OfType<ListBoxItem>().Class(":selected"),
            x => x.OfType<ListBoxItem>().Class(":selected").Class(":pointerover"),
            x => x.OfType<ListBoxItem>().Class(":selected").Class(":focus"),
            x => x.OfType<ListBoxItem>().Class(":selected").Class(":pressed"),
        ];
        foreach (var state in chosen)
        {
            list.Styles.Add(new Avalonia.Styling.Style(x => state(x).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters =
                {
                    new Setter(ContentPresenter.BackgroundProperty, accent),
                    new Setter(ContentPresenter.ForegroundProperty, Brushes.White),
                },
            });
        }
        list.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>().Class(":selected").Descendant().OfType<Button>())
        {
            Setters = { new Setter(TemplatedControl.ForegroundProperty, Brushes.White) },
        });
    }

    /// <summary>Shows <paramref name="document"/>'s shapes. The rows are rebuilt only when one
    /// changed, since this runs on every change to the canvas, a drag's every step included.</summary>
    public void Show(Document document, Guid? selection)
    {
        // Rebuilding drops the focused row; the keys go back to the list, not to nowhere.
        var hadKeys = List.IsKeyboardFocusWithin;
        try { Update(document, selection); }
        finally { if (hadKeys && !List.IsKeyboardFocusWithin) FocusList(); }
    }

    /// <summary>The keys to the chosen row, or to the list when none is chosen: for opening the
    /// panel from the keyboard, and after the rows are rebuilt.</summary>
    internal void FocusList()
    {
        var index = List.SelectedIndex;
        Control target = index >= 0 && index < rows.Count ? rows[index].Item : List;
        // Once the new rows are laid out; a row not yet in the tree cannot take the keys.
        Dispatcher.UIThread.Post(() =>
        {
            if (!target.Focus(NavigationMethod.Directional)) List.Focus(NavigationMethod.Directional);
        });
    }

    private void Update(Document document, Guid? selection)
    {
        var now = document.Annotations.Reverse()
            .Select(a => new Shape(a.Id, a.Tool, document.LayerName(a.Id), a.IsLocked, a.IsHidden)).ToArray();
        if (!now.SequenceEqual(shown))
        {
            // Only names changed, as on every key typed into a text: renamed where they stand.
            if (now.Length == shown.Length && now.Zip(shown).All(p => p.First with { Name = "" } == p.Second with { Name = "" }))
                Rename(now);
            else
                Rebuild(now);
            shown = now;
        }
        var index = rows.FindIndex(row => row.Id == selection);
        if (List.SelectedIndex == index) return;
        syncing = true;
        List.SelectedIndex = index;
        syncing = false;
    }

    private void Rename(Shape[] now)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Name == now[i].Name) continue;
            rows[i] = row with { Name = now[i].Name };
            row.Label.Text = now[i].Name;
            Describe(rows[i]);
        }
    }

    /// <summary>The row and its buttons as a screen reader names them.</summary>
    private static void Describe(LayerRow row)
    {
        AutomationProperties.SetName(row.Item, row.Name + (row.IsLocked ? ", locked" : "") + (row.IsHidden ? ", hidden" : ""));
        AutomationProperties.SetName(row.DuplicateButton, $"Duplicate {row.Name}");
        AutomationProperties.SetName(row.DeleteButton, $"Delete {row.Name}");
        AutomationProperties.SetName(row.HideButton, $"{(row.IsHidden ? "Show" : "Hide")} {row.Name}");
        AutomationProperties.SetName(row.LockButton, $"{(row.IsLocked ? "Unlock" : "Lock")} {row.Name}");
    }

    private void Rebuild(Shape[] now)
    {
        syncing = true;
        List.Items.Clear();
        rows = [.. now.Select(MakeRow)];
        foreach (var row in rows) List.Items.Add(row.Item);
        syncing = false;
        count.Text = rows.Count.ToString(CultureInfo.InvariantCulture);
        EmptyNote.IsVisible = rows.Count == 0;
    }

    private LayerRow MakeRow(Shape shape)
    {
        var (id, name) = (shape.Id, shape.Name);
        var icon = Glyphs.Icon(ToolIcons.For(shape.Tool), 16);
        icon.Opacity = shape.IsHidden ? 0.4 : 1;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var label = new TextBlock
        {
            Text = name,
            FontSize = 13,
            Margin = new Thickness(8, 0, 4, 0),
            Opacity = shape.IsHidden ? 0.4 : 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var duplicate = RowButton(ToolIcons.Duplicate, $"Duplicate {name}", "Duplicate (Ctrl+D)", () => DuplicateRequested?.Invoke(id));
        var hide = RowButton(shape.IsHidden ? ToolIcons.EyeOff : ToolIcons.Eye, $"{(shape.IsHidden ? "Show" : "Hide")} {name}",
                             shape.IsHidden ? "Show" : "Hide", () => HideChanged?.Invoke(id, !shape.IsHidden));
        var lockButton = RowButton(shape.IsLocked ? ToolIcons.Lock : ToolIcons.Unlock, $"{(shape.IsLocked ? "Unlock" : "Lock")} {name}",
                                   shape.IsLocked ? "Unlock (Ctrl+L)" : "Lock (Ctrl+L)", () => LockChanged?.Invoke(id, !shape.IsLocked));
        lockButton.Opacity = shape.IsLocked ? 1 : 0.5;
        var delete = RowButton(ToolIcons.Delete, $"Delete {name}", "Delete, or the Delete key", () => DeleteRequested?.Invoke(id));
        // A locked shape cannot be deleted until it is unlocked.
        delete.IsEnabled = !shape.IsLocked;
        duplicate.IsVisible = false;
        delete.IsVisible = false;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"), Height = RowHeight };
        foreach (var (control, column) in new (Control, int)[] { (icon, 0), (label, 1), (duplicate, 2), (delete, 3), (hide, 4), (lockButton, 5) })
        {
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }
        var item = new ListBoxItem
        {
            Content = grid,
            Padding = new Thickness(8, 0, 4, 0),
            MinHeight = RowHeight,
            Opacity = id == lifted ? 0.4 : 1,
        };
        // Duplicate and delete show under the pointer and on the chosen row; the canvas borders
        // the shape.
        void ShowActions(bool shown) => duplicate.IsVisible = delete.IsVisible = shown;
        item.PointerEntered += (_, _) =>
        {
            ShowActions(true);
            HoverChanged?.Invoke(id);
        };
        item.PointerExited += (_, _) =>
        {
            ShowActions(item.IsSelected);
            HoverChanged?.Invoke(null);
        };
        item.PropertyChanged += (_, e) =>
        {
            if (e.Property == ListBoxItem.IsSelectedProperty) ShowActions(item.IsSelected || item.IsPointerOver);
        };
        var row = new LayerRow(id, name, shape.IsLocked, shape.IsHidden, item, duplicate, delete, hide, lockButton, label);
        Describe(row);
        return row;
    }

    private static Button RowButton(string icon, string name, string tip, Action action)
    {
        var button = new Button
        {
            Content = Glyphs.Icon(icon, 16),
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            // The row is what takes the keys; Space and Ctrl+L reach these from it.
            Focusable = false,
        };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, name);
        button.Click += (_, e) =>
        {
            action();
            e.Handled = true;
        };
        return button;
    }

    // Keys

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            if (e.Key is not (Key.Z or Key.Y)) return;
            var redo = e.Key == Key.Y || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            (redo ? RedoRequested : UndoRequested)?.Invoke();
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None) return;
        switch (e.Key)
        {
            case Key.Space when List.SelectedIndex >= 0 && List.SelectedIndex < rows.Count:
                var row = rows[List.SelectedIndex];
                HideChanged?.Invoke(row.Id, !row.IsHidden);
                e.Handled = true;
                break;
            case Key.Escape when lifted is not null:
                EndDrag(dropped: false);
                e.Handled = true;
                break;
            case Key.Escape:
                CloseRequested?.Invoke();
                e.Handled = true;
                break;
        }
    }

    // Dragging a row

    /// <summary>Moves the shape of <paramref name="id"/> to above row <paramref name="row"/>,
    /// counted top first, <c>Rows.Count</c> being below the last. The document wants a place in
    /// its list, bottom first, once the shape has left its old one.</summary>
    internal void DragTo(Guid id, int row)
    {
        var from = rows.FindIndex(r => r.Id == id);
        var to = from < row ? row - 1 : row;
        if (from < 0 || to == from) return;
        DraggedTo?.Invoke(id, rows.Count - 1 - to);
    }

    /// <summary>A drag starts only on a row's body: not on its buttons, nor on the list's
    /// scroll bar, which a long list shows over the rows.</summary>
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(List).Properties.IsLeftButtonPressed || RowAt(e) is not { } index) return;
        var source = e.Source as Visual;
        if (source?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null
            || source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        press = (rows[index].Id, e.GetPosition(List));
    }

    private void OnDragged(object? sender, PointerEventArgs e)
    {
        if (press is not { } held) return;
        // A release the list never heard about puts the shape back.
        if (!e.GetCurrentPoint(List).Properties.IsLeftButtonPressed)
        {
            EndDrag(dropped: false);
            return;
        }
        var at = e.GetPosition(List);
        // A few pixels of travel before it counts as a drag, so a click never moves anything.
        if (lifted is null && Math.Abs(at.Y - held.Start.Y) < 4) return;
        if (lifted is null) Lift(rows.First(row => row.Id == held.Id));
        dragging = e.Pointer;
        e.Pointer.Capture(List);
        DragGhost.Margin = new Thickness(4, at.Y - RowHeight / 2, 4, 0);
        // Over a row, its upper half lands the shape above it and its lower half below; the
        // row and its shape move there at once.
        DragTo(held.Id, Math.Clamp((int)Math.Round(FromFirstRow(e) / RowHeight), 0, rows.Count));
        e.Handled = true;
    }

    /// <summary>The dragged row's icon and name on a raised card that follows the pointer, and
    /// the row itself faded where it stands.</summary>
    private void Lift(LayerRow row)
    {
        lifted = row.Id;
        row.Item.Opacity = 0.4;
        var shape = shown.First(s => s.Id == row.Id);
        var icon = Glyphs.Icon(ToolIcons.For(shape.Tool), 16);
        icon.VerticalAlignment = VerticalAlignment.Center;
        DragGhost.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { icon, new TextBlock { Text = row.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center } },
        };
        DragGhost.IsVisible = true;
    }

    /// <summary>Kept where it stands when <paramref name="dropped"/>, put back otherwise.</summary>
    private void EndDrag(bool dropped)
    {
        var wasLifted = lifted is not null;
        press = null;
        lifted = null;
        dragging?.Capture(null);
        dragging = null;
        DragGhost.IsVisible = false;
        foreach (var row in rows) row.Item.Opacity = 1;
        if (!wasLifted) return;
        if (dropped) Dropped?.Invoke();
        else DragCancelled?.Invoke();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (press is null) return;
        if (lifted is null)
        {
            press = null;
            Clicked?.Invoke();
            return;
        }
        // Let go over the list it stays; anywhere else it goes back, as on the Mac.
        EndDrag(dropped: new Avalonia.Rect(List.Bounds.Size).Contains(e.GetPosition(List)));
        e.Handled = true;
    }

    /// <summary>How far below the first row's top the pointer is, wherever the list is scrolled.</summary>
    private double FromFirstRow(PointerEventArgs e) => rows.Count == 0 ? -1 : e.GetPosition(rows[0].Item).Y;

    private int? RowAt(PointerEventArgs e)
    {
        var y = FromFirstRow(e);
        var index = (int)Math.Floor(y / RowHeight);
        return y >= 0 && index < rows.Count ? index : null;
    }
}
