using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SkiaSharp;
using Tinysnap.Core;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class OpenFileTests
{
    /// <summary>A 400 by 300 picture written to a file of its own, as a PNG at 144 DPI or a JPEG.</summary>
    private static string Picture(string extension)
    {
        var path = Path.Combine(TemporaryFolder(), $"Receipt{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var image = CanvasHost.Blank(400, 300).Image;
        var bytes = extension == ".png" ? Png.Encode(image, 144)! : image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [AvaloniaFact]
    public void APngOpensInAnEditorAtItsOwnScale()
    {
        var setup = Launch();
        setup.Controller.OpenFiles([Picture(".png")]);
        var editor = Assert.Single(setup.Controller.Editors);
        var capture = editor.Canvas.Session.Display.Capture;
        Assert.Equal((new Size(400, 300), 2.0), (capture.PixelSize, capture.Scale));
        Assert.Equal("Receipt.png", editor.Title);
        // A file opened is not a capture: nothing is kept in the library for it.
        Assert.Null(editor.Entry);
        Assert.Empty(setup.Library.Entries());
    }

    [AvaloniaFact]
    public void AnyOtherPictureOpensAtOnePixelAPoint()
    {
        var setup = Launch();
        setup.Controller.OpenFiles([Picture(".jpg")]);
        Assert.Equal(1, Assert.Single(setup.Controller.Editors).Canvas.Session.Display.Capture.Scale);
    }

    [AvaloniaFact]
    public void AFileThatIsNoPictureIsSkipped()
    {
        var setup = Launch();
        var notes = Path.Combine(TemporaryFolder(), "notes.png");
        Directory.CreateDirectory(Path.GetDirectoryName(notes)!);
        File.WriteAllText(notes, "not a picture");
        setup.Controller.OpenFiles([notes, Path.Combine(TemporaryFolder(), "gone.png"), Picture(".png")]);
        Assert.Single(setup.Controller.Editors);
    }

    [AvaloniaFact]
    public void OpeningTinysnapAgainWithAFileOpensIt()
    {
        var setup = Launch();
        setup.Platform.Reopen(Picture(".png"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Receipt.png", Assert.Single(setup.Controller.Editors).Title);
        Assert.Null(setup.Controller.OpenSettings);
    }
}
