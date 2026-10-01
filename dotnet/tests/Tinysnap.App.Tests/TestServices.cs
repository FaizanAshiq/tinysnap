using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Tinysnap.App.Capturing;
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

    /// <summary>What every confirmation is answered.</summary>
    public bool Confirmation { get; set; }

    public Task<bool> Confirm(Window owner, string message, string detail, string action) => Task.FromResult(Confirmation);

    public Task<CloseChoice> AskToSave(Window owner, bool libraryFailed = false)
    {
        Asked++;
        return Task.FromResult(Answer);
    }
}

/// <summary>A controller on one Retina monitor 400 by 300 pixels, with a library, clipboard,
/// dialogs, files and timers of its own for a test to look at.</summary>
internal sealed record AppSetup(CaptureController Controller, LibraryStore Library, FakeClipboard Clipboard, FakeDialogs Dialogs,
                                FakeTime Time, FakeFiles Files, FakePlatform Platform);

internal static class TestServices
{
    /// <param name="picker">The system's window picker, for a platform that cannot list windows.</param>
    public static AppSetup Launch(Preferences? preferences = null, Func<Task<Capture?>>? picker = null)
    {
        var retina = Screens.Frozen(new Tinysnap.Core.Rect(0, 0, 400, 300), 2, SkiaSharp.SKColors.Blue);
        var platform = new FakePlatform(new FakeScreenCapture(() => Screens.Desktop(retina), () => new Tinysnap.Core.Point(100, 100),
                                                              pickWindow: picker));
        var library = new LibraryStore(TemporaryFolder());
        var dialogs = new FakeDialogs();
        var time = new FakeTime();
        var controller = new CaptureController(platform, Store(preferences), library, dialogs, time);
        return new AppSetup(controller, library, (FakeClipboard)platform.Clipboard, dialogs, time, (FakeFiles)platform.Files, platform);
    }

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
                                  dialogs ?? new FakeDialogs(), pin is null ? null : (exported, keepsSize, _) => pin(exported, keepsSize));
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
        // An editor the controller opened may not have laid out at its placed size yet.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();
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
