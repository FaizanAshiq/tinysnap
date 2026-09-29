using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;

namespace Tinysnap.App.Tests;

public class DialogTests
{
    private static Button ButtonIn(Window dialog, string label) =>
        Assert.Single(Descendants(dialog).OfType<Button>(), b => (string?)b.Content == label);

    private static IEnumerable<Control> Descendants(Control control)
    {
        yield return control;
        var children = control switch
        {
            Panel panel => panel.Children.AsEnumerable(),
            ContentControl { Content: Control child } => [child],
            Decorator { Child: { } child } => [child],
            _ => [],
        };
        foreach (var child in children)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    [AvaloniaTheory]
    [InlineData("Save", CloseChoice.Save)]
    [InlineData("Discard", CloseChoice.Discard)]
    [InlineData("Cancel", CloseChoice.Cancel)]
    public async Task TheSavePromptOffersSaveDiscardAndCancel(string label, CloseChoice expected)
    {
        var owner = new Window();
        owner.Show();
        var asking = new AvaloniaDialogs().AskToSave(owner);
        var dialog = Assert.Single(owner.OwnedWindows);
        ButtonIn(dialog, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(expected, await asking);
    }

    [AvaloniaFact]
    public async Task ClosingThePromptItselfCancels()
    {
        var owner = new Window();
        owner.Show();
        var asking = new AvaloniaDialogs().AskToSave(owner);
        Assert.Single(owner.OwnedWindows).Close();
        Assert.Equal(CloseChoice.Cancel, await asking);
    }
}
