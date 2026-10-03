using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Tinysnap.App.Editing;

namespace Tinysnap.App;

/// <summary>Every capture and the library as tabs of one window, as the Mac does with its own
/// window tabs. Avalonia has none, so each stays a window of its own, and only the chosen one
/// shows, in the place and at the size of the one before it, with a strip of tabs along its
/// top. Closing the shown tab shows its neighbour in its place.</summary>
internal sealed class WindowTabs(Action<Window> raise)
{
    private readonly List<Window> members = [];

    public IReadOnlyList<Window> Members => members;

    /// <summary>The tab showing, which is the one window of them on screen.</summary>
    public Window? Shown { get; private set; }

    /// <summary>A tab joined, left or was chosen: every strip draws itself again.</summary>
    public event Action? Changed;

    /// <summary>Shows <paramref name="window"/> as the chosen tab, joining it first when new.</summary>
    public void Show(Window window)
    {
        if (!members.Contains(window))
        {
            members.Add(window);
            window.Closed += (_, _) => Leave(window);
        }
        var previous = Shown;
        Shown = window;
        var taking = previous is { IsVisible: true } && previous != window;
        if (taking) TakePlace(window, previous!);
        if (!window.IsVisible) window.Show();
        // Again once shown: a hidden window's place is put back as it was when it shows.
        if (taking) TakePlace(window, previous!);
        raise(window);
        // After the new one is up, so the space never shows empty between them.
        if (previous is not null && previous != window) previous.Hide();
        Changed?.Invoke();
    }

    private void Leave(Window window)
    {
        var index = members.IndexOf(window);
        if (index < 0) return;
        members.RemoveAt(index);
        if (Shown == window)
        {
            Shown = null;
            if (members.Count > 0)
            {
                var next = members[Math.Min(index, members.Count - 1)];
                TakePlace(next, window);
                Show(next);
                return;
            }
        }
        Changed?.Invoke();
    }

    /// <summary>The window takes the other one's place and size, maximised or not.</summary>
    private static void TakePlace(Window window, Window other)
    {
        window.WindowState = other.WindowState == WindowState.Minimized ? WindowState.Normal : other.WindowState;
        if (window.WindowState != WindowState.Normal) return;
        window.Position = other.Position;
        window.Width = other.ClientSize.Width;
        window.Height = other.ClientSize.Height;
    }
}

/// <summary>The tabs along the top of each window in a <see cref="WindowTabs"/>: one per capture
/// and the library, the window's own lit, each with a button that closes it. Shown only with
/// two or more.</summary>
internal sealed class WindowTabStrip : Border
{
    private readonly WindowTabs tabs;
    private readonly Window owner;
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly List<ToggleButton> tabButtons = [];
    private readonly List<Button> closeButtons = [];

    internal IReadOnlyList<ToggleButton> Tabs => tabButtons;
    internal IReadOnlyList<Button> CloseButtons => closeButtons;

    public WindowTabStrip(WindowTabs tabs, Window owner)
    {
        this.tabs = tabs;
        this.owner = owner;
        Padding = new Thickness(6, 4);
        this[!BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundChromeMediumBrush");
        Child = new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetName(this, "Tabs");
        tabs.Changed += Rebuild;
        owner.Closed += (_, _) => tabs.Changed -= Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        IsVisible = tabs.Members.Count > 1;
        row.Children.Clear();
        tabButtons.Clear();
        closeButtons.Clear();
        foreach (var member in tabs.Members)
        {
            var title = member.Title ?? "";
            var close = new Button
            {
                Content = Glyphs.Icon(ToolIcons.Close, 12),
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(close, "Close");
            AutomationProperties.SetName(close, "Close " + title);
            close.Click += (_, e) =>
            {
                member.Close();
                e.Handled = true;
            };
            var tab = new ToggleButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            MaxWidth = 200,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        close,
                    },
                },
                IsChecked = member == owner,
                Padding = new Thickness(10, 3, 4, 3),
                CornerRadius = new CornerRadius(6),
            };
            AutomationProperties.SetName(tab, title);
            tab.Click += (_, _) =>
            {
                // A tab stays lit while it is the one showing; choosing another shows that one.
                tab.IsChecked = member == owner;
                if (member != owner) tabs.Show(member);
            };
            tabButtons.Add(tab);
            closeButtons.Add(close);
            row.Children.Add(tab);
        }
    }
}
