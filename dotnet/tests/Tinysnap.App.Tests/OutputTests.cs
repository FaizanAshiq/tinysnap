using Tinysnap.Core;
using Tinysnap.Dev;

namespace Tinysnap.App.Tests;

public class OutputTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 41, 12, TimeSpan.Zero);

    private static string Folder() => Path.Combine(Path.GetTempPath(), $"tinysnap-output-{Guid.NewGuid()}");

    private static byte[] Png() => Output.Export(new Document(CanvasHost.Blank(40, 30)), ExportScale.Native)!.Value.Png;

    [Fact]
    public void SavingNamesTheFileForItsTimeAndNumbersClashes()
    {
        var folder = Folder();
        var first = Output.Save(Png(), folder, Now);
        var second = Output.Save(Png(), folder, Now);
        var name = FileNaming.FileName(Now, _ => false);
        Assert.Equal(Path.Combine(folder, name), first);
        Assert.Equal(Path.Combine(folder, name[..^4] + " 2.png"), second);
        Assert.True(File.Exists(first) && File.Exists(second));
    }

    [Fact]
    public void SavingCreatesAMissingSaveFolder()
    {
        // Windows has no Pictures\Screenshots until its own first screenshot.
        var folder = Path.Combine(Folder(), "Pictures", "Screenshots");
        Assert.True(File.Exists(Output.Save(Png(), folder, Now)));
    }

    [Fact]
    public void ASaveThatCannotWriteSaysWhere()
    {
        // A file where the folder should be.
        var folder = Folder();
        File.WriteAllText(folder, "not a folder");
        var error = Assert.Throws<OutputException>(() => Output.Save(Png(), folder, Now));
        Assert.Contains(folder, error.Message);
    }

    [Fact]
    public void CopyPutsTheExportAtTheSettingsScale()
    {
        var document = new Document(CanvasHost.Blank(800, 600, scale: 2));
        var (exported, png) = Output.Export(document, ExportScale.OneX)!.Value;
        var clipboard = new FakeClipboard();
        Assert.True(Output.Copy(clipboard, exported, png));
        Assert.Equal(400, clipboard.Image!.Width);
        Assert.Equal(72, clipboard.Dpi);
        Assert.Equal(400, Tinysnap.Core.Png.Decode(clipboard.Png!)!.Value.Image.Width);
    }

    [Fact]
    public void ATemporaryFileHoldsThePng()
    {
        var png = Png();
        var path = Output.TemporaryFile(png, Now);
        Assert.NotNull(path);
        Assert.Equal(png, File.ReadAllBytes(path));
        Assert.EndsWith(".png", path);
    }
}
