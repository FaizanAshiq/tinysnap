using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Tinysnap.App.Capturing;
using Tinysnap.Core;

namespace Tinysnap.App.Settings;

/// <summary>A short stack of standard controls. Anything not here is still reachable by editing
/// preferences.json, which is why this window stays small on purpose.</summary>
internal sealed class SettingsWindow : Window
{
    private const double LabelWidth = 130, ControlWidth = 278;

    private readonly CaptureController captures;
    private readonly TextBlock librarySize = Note();
    private readonly TextBlock libraryProblem = new()
    {
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = ControlWidth,
        Foreground = Brushes.Firebrick,
    };
    /// <summary>Set while values are put into the controls, so doing so writes nothing back.</summary>
    private bool loading;

    internal IReadOnlyDictionary<HotKeyAction, HotKeyRecorder> Recorders { get; }
    internal TextBlock SaveFolder { get; } = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    internal ComboBox Export { get; } = Choice("Full resolution", "1x, one pixel per point");
    internal ComboBox AfterCapture { get; } = Choice("Open the editor", "Show a thumbnail");
    internal CheckBox KeepLibrary { get; } = new() { Content = "Keep captures in the library for 30 days" };
    internal NumericUpDown Delay { get; } = new() { Minimum = Preferences.DelayMin, Maximum = 10, Increment = 1, FormatString = "0", Width = 120 };
    internal CheckBox OpenAtLogin { get; } = new() { Content = "Open Tinysnap when you log in" };
    internal CheckBox ShowTrayIcon { get; } = new() { Content = "Show the tray icon" };

    public SettingsWindow(CaptureController captures)
    {
        this.captures = captures;
        Title = "Tinysnap Settings";
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var recorders = new Dictionary<HotKeyAction, HotKeyRecorder>();
        var rows = new List<(string Label, Control Control)>();
        foreach (var action in HotkeyRegistrar.Available)
        {
            var recorder = new HotKeyRecorder(null, $"{action.Title()} hotkey") { Width = ControlWidth, Record = binding => Record(action, binding) };
            recorder.RecordingChanged += recording =>
            {
                if (recording) captures.Hotkeys.Pause();
                else captures.Hotkeys.Resume();
            };
            recorders[action] = recorder;
            rows.Add((action.Title(), recorder));
        }
        Recorders = recorders;

        var choose = new Button { Content = "Choose" };
        choose.Click += (_, _) => _ = ChooseFolder();
        DockPanel.SetDock(choose, Dock.Right);
        rows.Add(("Save folder", new DockPanel { Width = ControlWidth, LastChildFill = true, Children = { choose, SaveFolder } }));

        Export.Width = ControlWidth;
        ToolTip.SetTip(Export, "The size every capture starts at. The Size button in the editor changes one capture");
        rows.Add(("Export", Export));
        AfterCapture.Width = ControlWidth;
        rows.Add(("After a capture", AfterCapture));

        var open = new Button { Content = "Open Library" };
        open.Click += (_, _) => captures.ShowLibrary();
        var clear = new Button { Content = "Clear Library..." };
        clear.Click += (_, _) => _ = ClearLibrary();
        rows.Add(("Library", new StackPanel
        {
            Spacing = 6,
            Children =
            {
                KeepLibrary,
                Note("Keeps what is under blurs and erases too"),
                librarySize,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, clear } },
                libraryProblem,
            },
        }));

        rows.Add(("Delay", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { Delay, new TextBlock { Text = "seconds", VerticalAlignment = VerticalAlignment.Center } },
        }));
        rows.Add(("", OpenAtLogin));
        ToolTip.SetTip(ShowTrayIcon, "Hidden, the hotkeys still work, and opening Tinysnap again shows Settings");
        rows.Add(("", ShowTrayIcon));

        var grid = new Grid
        {
            Margin = new Thickness(20),
            ColumnDefinitions = new ColumnDefinitions($"{LabelWidth},12,Auto"),
            RowSpacing = 12,
        };
        for (var index = 0; index < rows.Count; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var (text, control) = rows[index];
            var label = new TextBlock
            {
                Text = text,
                TextAlignment = TextAlignment.Right,
                // On the first line of the control, so a label beside several lines sits with the first.
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 0, 0),
            };
            Grid.SetRow(label, index);
            Grid.SetRow(control, index);
            Grid.SetColumn(control, 2);
            grid.Children.Add(label);
            grid.Children.Add(control);
        }
        Content = grid;

        Export.SelectionChanged += (_, _) => Change(p => p with { ExportScale = Export.SelectedIndex == 1 ? ExportScale.OneX : ExportScale.Native });
        AfterCapture.SelectionChanged += (_, _) =>
            Change(p => p with { AfterCapture = AfterCapture.SelectedIndex == 1 ? Core.AfterCapture.Thumbnail : Core.AfterCapture.Editor });
        KeepLibrary.IsCheckedChanged += (_, _) => Change(p => p with { KeepLibrary = KeepLibrary.IsChecked == true });
        Delay.ValueChanged += (_, _) => Change(p => p with { DelaySeconds = (int)(Delay.Value ?? Preferences.DelayMin) });
        ShowTrayIcon.IsCheckedChanged += (_, _) => Change(p => p with { ShowTrayIcon = ShowTrayIcon.IsChecked == true });
        OpenAtLogin.IsCheckedChanged += (_, _) => ChangeLogin();

        captures.Hotkeys.Changed += Load;
        captures.LibraryChanged += RefreshLibrary;
        Closed += (_, _) =>
        {
            foreach (var recorder in Recorders.Values) recorder.StopRecording();
            captures.Hotkeys.Changed -= Load;
            captures.LibraryChanged -= RefreshLibrary;
        };
        Load();
    }

    private static TextBlock Note(string text = "") => new()
    {
        Text = text,
        FontSize = 12,
        [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush"),
    };

    private static ComboBox Choice(params string[] items)
    {
        var box = new ComboBox();
        foreach (var item in items) box.Items.Add(item);
        return box;
    }

    /// <summary>Every control set from the preferences as they are now.</summary>
    private void Load()
    {
        loading = true;
        var preferences = captures.Preferences.Current;
        foreach (var (action, recorder) in Recorders)
        {
            recorder.Binding = preferences.HotKeys[action];
            recorder.IsTaken = captures.Hotkeys.Taken.Contains(action);
        }
        SaveFolder.Text = preferences.SaveFolderPath;
        ToolTip.SetTip(SaveFolder, preferences.SaveFolderPath);
        Export.SelectedIndex = preferences.ExportScale == ExportScale.OneX ? 1 : 0;
        AfterCapture.SelectedIndex = preferences.AfterCapture == Core.AfterCapture.Thumbnail ? 1 : 0;
        KeepLibrary.IsChecked = preferences.KeepLibrary;
        Delay.Value = Math.Clamp(preferences.DelaySeconds, Preferences.DelayMin, 10);
        OpenAtLogin.IsChecked = captures.Startup.IsEnabled;
        ShowTrayIcon.IsChecked = preferences.ShowTrayIcon;
        loading = false;
        RefreshLibrary();
    }

    private void RefreshLibrary()
    {
        librarySize.Text = "Uses " + Bytes(captures.Library.Size());
        libraryProblem.Text = captures.LibraryError is { } problem ? $"Captures are not being kept: {problem}" : "";
        libraryProblem.IsVisible = captures.LibraryError is not null;
    }

    /// <summary>"12.3 MB", in the thousands Windows and macOS count files in.</summary>
    private static string Bytes(long bytes)
    {
        if (bytes < 1000) return $"{bytes} bytes";
        string[] units = ["KB", "MB", "GB", "TB"];
        var value = bytes / 1000.0;
        var unit = 0;
        for (; value >= 1000 && unit < units.Length - 1; unit++) value /= 1000;
        return $"{value.ToString("0.#", CultureInfo.CurrentCulture)} {units[unit]}";
    }

    private void Change(Func<Preferences, Preferences> change)
    {
        if (!loading) captures.Preferences.Update(change);
    }

    /// <summary>Refuses a combination another Tinysnap action already uses.</summary>
    private bool Record(HotKeyAction action, HotKeyBinding? binding)
    {
        var hotkeys = captures.Preferences.Current.HotKeys;
        if (binding is not null && hotkeys.ActionUsing(binding) is { } owner && owner != action) return false;
        captures.Preferences.Update(p => p with { HotKeys = p.HotKeys.With(action, binding) });
        return true;
    }

    /// <summary>Asked of Windows every time rather than stored, so it never disagrees with the
    /// startup list in Windows' own settings.</summary>
    private void ChangeLogin()
    {
        if (loading) return;
        var wanted = OpenAtLogin.IsChecked == true;
        if (captures.Startup.SetEnabled(wanted)) return;
        _ = captures.Services.Dialogs.Tell(this, "Windows did not change whether Tinysnap opens when you log in.");
        loading = true;
        OpenAtLogin.IsChecked = captures.Startup.IsEnabled;
        loading = false;
    }

    private async Task ChooseFolder()
    {
        var preferences = captures.Preferences.Current;
        var start = Directory.Exists(preferences.SaveFolderPath)
            ? await StorageProvider.TryGetFolderFromPathAsync(preferences.SaveFolderPath)
            : null;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a Save Folder",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Kept as ~ when it is under the profile folder, as the default is, so the setting still
        // reads right on another machine.
        var stored = path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + path[home.Length..] : path;
        captures.Preferences.Update(p => p with { SaveFolder = stored });
        Load();
    }

    /// <summary>Asks first, since nothing cleared comes back. Captures open in an editor stay.</summary>
    internal async Task ClearLibrary()
    {
        var sure = await captures.Services.Dialogs.Confirm(this, "Clear the library?",
            "Every kept capture is deleted, except any open in an editor. This cannot be undone.", "Clear Library");
        if (!sure) return;
        captures.Library.Clear(captures.OpenEntryNames);
        captures.NotifyLibraryChanged();
        RefreshLibrary();
    }
}
