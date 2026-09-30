using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Tinysnap.Core;
using Tinysnap.Dev;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class EditorOutputTests
{
    [AvaloniaFact]
    public void CtrlCCopiesAndTheEditorStays()
    {
        var clipboard = new FakeClipboard();
        var editor = Editor(Make(clipboard));
        Draw(editor);
        Press(editor, Key.C, RawInputModifiers.Control, "c");
        Assert.NotNull(clipboard.Png);
        Assert.Equal(400, clipboard.Image!.Width);
        Assert.True(editor.IsVisible);
        Assert.False(editor.Canvas.Session.IsUnsaved);
    }

    [AvaloniaFact]
    public void CtrlSSavesIntoTheSaveFolder()
    {
        var folder = TemporaryFolder();
        var editor = Editor(Make(saveFolder: folder));
        Draw(editor);
        Press(editor, Key.S, RawInputModifiers.Control, "s");
        var saved = Assert.Single(Directory.GetFiles(folder));
        Assert.Equal(400, Png.Decode(File.ReadAllBytes(saved))!.Value.Image.Width);
        Assert.False(editor.Canvas.Session.IsUnsaved);
    }

    [AvaloniaFact]
    public void ClosingWithUnsavedEditsAsksAndCancelKeepsTheEditor()
    {
        var dialogs = new FakeDialogs(CloseChoice.Cancel);
        var editor = Editor(Make(dialogs: dialogs));
        var closed = false;
        editor.Closed += (_, _) => closed = true;
        Draw(editor);
        editor.Close();
        Assert.Equal(1, dialogs.Asked);
        Assert.False(closed);

        dialogs.Answer = CloseChoice.Discard;
        editor.Close();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void SaveInThePromptSavesThenCloses()
    {
        var folder = TemporaryFolder();
        var editor = Editor(Make(dialogs: new FakeDialogs(CloseChoice.Save), saveFolder: folder));
        var closed = false;
        editor.Closed += (_, _) => closed = true;
        Draw(editor);
        editor.Close();
        Assert.Single(Directory.GetFiles(folder));
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void ACopiedOrUntouchedCaptureClosesWithoutAsking()
    {
        var dialogs = new FakeDialogs();
        var untouched = Editor(Make(dialogs: dialogs));
        untouched.Close();

        var copied = Editor(Make(dialogs: dialogs));
        Draw(copied);
        Press(copied, Key.C, RawInputModifiers.Control, "c");
        copied.Close();
        Assert.Equal(0, dialogs.Asked);
    }

    [AvaloniaFact]
    public void PinClosesTheEditorAndOpensAPin()
    {
        ExportedImage? pinned = null;
        var editor = Editor(Make(pin: (exported, _) => pinned = exported));
        var closed = false;
        editor.Closed += (_, _) => closed = true;
        Draw(editor);
        // Only the press: the window is gone before a release could reach it.
        editor.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.None, "p");
        Assert.Equal(400, pinned!.Image.Width);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void TheDragHandleWritesAPng()
    {
        var editor = Editor(Make());
        Draw(editor);
        var path = editor.DragFile();
        Assert.NotNull(path);
        Assert.Equal(400, Png.Decode(File.ReadAllBytes(path))!.Value.Image.Width);
    }

    [AvaloniaFact]
    public void TheToolbarStartsWithCopySaveDragAndPin()
    {
        var editor = Editor(Make());
        Assert.Equal(["Copy", "Save", "Drag out", "Copy Text", "Scan QR Code", "Pin and close"], editor.OutputButtons.Select(b => Avalonia.Automation.AutomationProperties.GetName(b)));
    }
}
