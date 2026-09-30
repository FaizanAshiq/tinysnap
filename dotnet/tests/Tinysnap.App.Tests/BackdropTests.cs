using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using SkiaSharp;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class BackdropPanelTests
{
    private static readonly Backdrop Last = Backdrop.Defaults with { Padding = BackdropPadding.Large, Shadow = BackdropShadow.Strong };

    /// <summary>An editor whose last backdrop is <see cref="Last"/>, with its Backdrop panel open,
    /// recording what it remembers.</summary>
    private static (EditorWindow Editor, List<Backdrop> Remembered) Open(BackdropWallpaper? wallpaper = null)
    {
        var remembered = new List<Backdrop>();
        var services = Make() with
        {
            Preferences = () => Preferences.Defaults with { Backdrop = Last },
            RememberBackdrop = remembered.Add,
            ReadWallpaper = () => wallpaper,
        };
        var editor = Editor(services);
        Click(editor.BackdropButton);
        return (editor, remembered);
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Backdrop? Shown(EditorWindow editor) => editor.Canvas.Session.Display.Backdrop;

    [AvaloniaFact]
    public void TheBackdropButtonOpensThePanel()
    {
        var (editor, _) = Open();
        Assert.Equal(StyleBarMode.Backdrop, editor.StyleBar.Mode);
        Assert.True(editor.StyleBar.IsVisible);
        Assert.Equal(["No backdrop", "Gradient from the capture", "Solid colour", "Desktop wallpaper", "Clear, see-through"],
                     editor.StyleBar.BackdropFillChips.Select(chip => Avalonia.Automation.AutomationProperties.GetName(chip)));
        Assert.True(editor.StyleBar.BackdropFillChips[0].IsChecked);
        Assert.Empty(editor.StyleBar.PaddingChips);
    }

    [AvaloniaFact]
    public void TurningTheBackdropOnBringsTheLastOneBack()
    {
        var (editor, remembered) = Open();
        Click(editor.StyleBar.BackdropFillChips[1]);
        Assert.Equal(Last, Shown(editor));
        Assert.Equal(Last, remembered[^1]);
        Assert.Equal(3, editor.StyleBar.PaddingChips.Count);
        Assert.True(editor.StyleBar.PaddingChips[2].IsChecked);
    }

    [AvaloniaFact]
    public void EachChipChangesItsPart()
    {
        var (editor, remembered) = Open();
        Click(editor.StyleBar.BackdropFillChips[1]);
        Click(editor.StyleBar.PaddingChips[0]);
        Assert.Equal(BackdropPadding.Small, Shown(editor)!.Padding);
        Click(editor.StyleBar.BackdropCornerChips[0]);
        Assert.Equal(CornerSize.Square, Shown(editor)!.Corners);
        Click(editor.StyleBar.ShadowChips[0]);
        Assert.Equal(BackdropShadow.None, Shown(editor)!.Shadow);
        Assert.Equal(Shown(editor), remembered[^1]);
        Click(editor.StyleBar.BackdropFillChips[0]);
        Assert.Null(Shown(editor));
    }

    [AvaloniaFact]
    public void ASolidFillTakesAColour()
    {
        var (editor, _) = Open();
        Click(editor.StyleBar.BackdropFillChips[2]);
        Assert.NotNull(editor.StyleBar.ColorButton);
        Click(editor.StyleBar.Swatches.First(s => (string?)s.Tag == "#34C759"));
        Assert.Equal("#34C759", Shown(editor)!.ColorHex);
        // The tool's own colour is left alone.
        Assert.NotEqual("#34C759", editor.Canvas.Session.ColorHex);
    }

    [AvaloniaFact]
    public void TheWallpaperFillReadsTheDesktop()
    {
        // Not disposed: the editor, still open after the test, goes on drawing it.
        var wallpaper = new BackdropWallpaper(Guid.NewGuid(), new PastedImage(SKImage.Create(new SKImageInfo(8, 8))));
        var (editor, _) = Open(wallpaper);
        Click(editor.StyleBar.BackdropFillChips[3]);
        Assert.Equal(wallpaper, Shown(editor)!.Wallpaper);
        Assert.Null(editor.StyleBar.WallpaperNote);
    }

    [AvaloniaFact]
    public void AnUnreadableWallpaperSaysTheGradientStandsIn()
    {
        var (editor, _) = Open();
        Click(editor.StyleBar.BackdropFillChips[3]);
        Assert.Equal(BackdropFill.Wallpaper, Shown(editor)!.Fill);
        Assert.Equal("Using the gradient", editor.StyleBar.WallpaperNote!.Text);
    }

    [AvaloniaFact]
    public void PickingAToolClosesThePanel()
    {
        var (editor, _) = Open();
        Press(editor, Key.R, symbol: "r");
        Assert.Equal(StyleBarMode.Tool, editor.StyleBar.Mode);
        Assert.NotEmpty(editor.StyleBar.CornerChips);
    }

    [AvaloniaFact]
    public void TheAppReadsTheDesktopPictureAndRemembersTheBackdrop()
    {
        var setup = Launch();
        using var desktop = SKImage.Create(new SKImageInfo(3200, 1800));
        setup.Files.WallpaperImage = desktop;
        setup.Controller.CaptureFullscreen();
        var editor = Assert.Single(setup.Controller.Editors);
        Click(editor.BackdropButton);
        Click(editor.StyleBar.BackdropFillChips[3]);
        var wallpaper = Shown(editor)!.Wallpaper!;
        // Read at no more than 1600 pixels across.
        Assert.Equal(1600, wallpaper.Image.Image.Width);
        Assert.Equal(BackdropFill.Wallpaper, setup.Controller.Preferences.Current.Backdrop.Fill);
        // Preferences keep the settings, never the picture.
        Assert.Null(setup.Controller.Preferences.Current.Backdrop.Wallpaper);
    }
}
