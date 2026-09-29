using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using Tinysnap.Dev;
using Point = Avalonia.Point;

namespace Tinysnap.App.Tests;

/// <summary>Answers every question with <see cref="Answer"/> and keeps every notice.</summary>
internal sealed class FakeDialogs(CloseChoice answer = CloseChoice.Cancel) : IDialogs
{
    public CloseChoice Answer { get; set; } = answer;
    public int Asked { get; private set; }
    public List<string> Told { get; } = [];

    public Task Tell(Window? owner, string message)
    {
        Told.Add(message);
        return Task.CompletedTask;
    }

    public Task<CloseChoice> AskToSave(Window owner)
    {
        Asked++;
        return Task.FromResult(Answer);
    }
}

internal static class TestServices
{
    public static string TemporaryFolder() => Path.Combine(Path.GetTempPath(), $"tinysnap-saves-{Guid.NewGuid()}");

    /// <summary>A store in a folder of its own holding <paramref name="preferences"/>.</summary>
    public static PreferencesStore Store(Preferences? preferences = null)
    {
        var store = new PreferencesStore(Path.Combine(TemporaryFolder(), "preferences.json"));
        if (preferences is not null) store.Update(_ => preferences);
        return store;
    }

    public static EditorServices Make(FakeClipboard? clipboard = null, FakeDialogs? dialogs = null, string? saveFolder = null,
                                      Action<ExportedImage, bool>? pin = null)
    {
        var folder = saveFolder ?? TemporaryFolder();
        return new EditorServices(clipboard ?? new FakeClipboard(), () => Preferences.Defaults with { SaveFolder = folder },
                                  dialogs ?? new FakeDialogs(), pin);
    }

    /// <summary>A shown editor on a blank 400 by 300 capture at 2x, laid out and focused.</summary>
    public static EditorWindow Editor(EditorServices services, Tool tool = Tool.Arrow)
    {
        var editor = new EditorWindow(new EditorSession(new Document(CanvasHost.Blank(400, 300)), tool), DateTimeOffset.Now, services);
        editor.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();
        editor.Canvas.Focus();
        return editor;
    }

    /// <summary>Draws a shape with the tool in hand, from canvas point (60, 50) to (110, 80): far
    /// enough inside the capture that its shadow does not grow it.</summary>
    public static void Draw(EditorWindow editor)
    {
        var canvas = editor.Canvas;
        var from = canvas.TranslatePoint(new Point(60, 50), editor)!.Value;
        var to = canvas.TranslatePoint(new Point(110, 80), editor)!.Value;
        editor.MouseDown(from, MouseButton.Left);
        editor.MouseMove(to, RawInputModifiers.LeftMouseButton);
        editor.MouseUp(to, MouseButton.Left);
    }

    public static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None, string? symbol = null)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, symbol);
        window.KeyRelease(key, modifiers, PhysicalKey.None, symbol);
    }
}
