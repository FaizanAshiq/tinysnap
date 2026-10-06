using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Tinysnap.App;

public enum CloseChoice { Save, Discard, Cancel }

/// <summary>The questions and notices the app puts to the person, behind an interface so tests
/// answer them without a modal window.</summary>
internal interface IDialogs
{
    /// <summary>A notice with OK, over <paramref name="owner"/> when there is one, and
    /// <paramref name="detail"/> under it when there is more to say.</summary>
    Task Tell(Window? owner, string message, string? detail = null);

    /// <summary>Save, Discard or Cancel, for closing a capture whose edits would be lost.
    /// <paramref name="libraryFailed"/> says why for a capture the library could not keep.</summary>
    Task<CloseChoice> AskToSave(Window owner, bool libraryFailed = false);

    /// <summary><paramref name="action"/> or Cancel, for something that cannot be undone. True
    /// only for the action; Cancel is the default.</summary>
    Task<bool> Confirm(Window owner, string message, string detail, string action);
}

internal sealed class AvaloniaDialogs : IDialogs
{
    public async Task Tell(Window? owner, string message, string? detail = null)
    {
        var dialog = Dialog(message, detail, [("OK", 0, true, true)]);
        if (owner is null) dialog.Show();
        else await dialog.ShowDialog<int?>(owner);
    }

    public async Task<CloseChoice> AskToSave(Window owner, bool libraryFailed = false)
    {
        var dialog = Dialog("Save this capture before closing?", libraryFailed
            ? "Tinysnap could not keep it in the library, so its annotations and crop are lost if you do not."
            : "Its annotations and crop are lost if you do not.",
        [
            ("Save", (int)CloseChoice.Save, true, false),
            ("Discard", (int)CloseChoice.Discard, false, false),
            ("Cancel", (int)CloseChoice.Cancel, false, true),
        ]);
        // Closed without an answer is Cancel: nothing is lost.
        return await dialog.ShowDialog<int?>(owner) is { } choice ? (CloseChoice)choice : CloseChoice.Cancel;
    }

    public async Task<bool> Confirm(Window owner, string message, string detail, string action)
    {
        var dialog = Dialog(message, detail, [(action, 1, false, false), ("Cancel", 0, true, true)]);
        return await dialog.ShowDialog<int?>(owner) == 1;
    }

    /// <summary>A small window with the message, an optional line under it, and buttons along the
    /// bottom right. Enter presses the default button and Esc the cancel one.</summary>
    private static Window Dialog(string message, string? detail, (string Label, int Result, bool IsDefault, bool IsCancel)[] buttons)
    {
        var dialog = new Window
        {
            Title = "Tinysnap",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MaxWidth = 460,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, result, isDefault, isCancel) in buttons)
        {
            var button = new Button { Content = label, IsDefault = isDefault, IsCancel = isCancel, MinWidth = 80 };
            button.Click += (_, _) => dialog.Close(result);
            row.Children.Add(button);
        }
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = message, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (detail is not null) body.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap });
        row.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(row);
        dialog.Content = body;
        return dialog;
    }
}
