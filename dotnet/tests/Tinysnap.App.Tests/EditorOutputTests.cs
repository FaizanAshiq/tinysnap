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
    public void CtrlCCopiesAndClosesTheEditor()
    {
        var clipboard = new FakeClipboard();
        var dialogs = new FakeDialogs();
        var editor = Editor(Make(clipboard, dialogs));
        var closed = false;
        editor.Closed += (_, _) => closed = true;
        Draw(editor);
        // Only the press: the window is gone before a release could reach it.
        editor.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.None, "c");
        Assert.Equal(400, clipboard.Image!.Width);
        Assert.True(closed);
        Assert.Equal(0, dialogs.Asked);
    }

    [AvaloniaFact]
    public void ACopyThatFailsKeepsTheEditor()
    {
        var editor = Editor(Make() with { Clipboard = new RefusingClipboard() });
        var closed = false;
        editor.Closed += (_, _) => closed = true;
        Draw(editor);
        editor.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.None, "c");
        Assert.False(closed);
        Assert.True(editor.Canvas.Session.IsUnsaved);
    }

    [AvaloniaFact]
    public void CtrlSSavesIntoTheSaveFolderAndClosesTheEditor()
    {
        var folder = TemporaryFolder();
        var editor = Editor(Make(saveFolder: folder));
        var closed = false;
        editor.Closed += (_, _) => closed = true;
        Draw(editor);
        editor.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.None, "s");
        var saved = Assert.Single(Directory.GetFiles(folder));
        Assert.Equal(400, Png.Decode(File.ReadAllBytes(saved))!.Value.Image.Width);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void ASaveThatFailsKeepsTheEditor()
    {
        // A file where the save folder should be, so the folder cannot be made.
        var notAFolder = Path.Combine(TemporaryFolder(), "taken");
        Directory.CreateDirectory(Path.GetDirectoryName(notAFolder)!);
        File.WriteAllText(notAFolder, "");
        var editor = Editor(Make(saveFolder: notAFolder));
        var closed = false;
        editor.Closed += (_, _) => closed = true;
        Draw(editor);
        editor.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.None, "s");
        Assert.False(closed);
        Assert.True(editor.Canvas.Session.IsUnsaved);
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
    public void AnUntouchedCaptureClosesWithoutAsking()
    {
        var dialogs = new FakeDialogs();
        Editor(Make(dialogs: dialogs)).Close();
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

    /// <summary>A clipboard another app is holding: every set fails.</summary>
    private sealed class RefusingClipboard : Tinysnap.Platform.IClipboard
    {
        public bool SetImage(SkiaSharp.SKImage image, byte[] png, double dpi) => false;

        public bool SetText(string text) => false;

        public uint ChangeCount => 0;
    }
}
