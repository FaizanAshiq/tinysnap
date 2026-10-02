using SkiaSharp;

namespace Tinysnap.Linux.Tests;

public class LinuxFilesTests
{
    private readonly List<string> calls = [];
    private int fileManagerExit;

    private (int, string) Record(string program, string[] args)
    {
        calls.Add($"{program} {string.Join(' ', args)}");
        return (program == "gdbus" ? fileManagerExit : 0, "");
    }

    [Fact]
    public void TrashGoesThroughGio()
    {
        var files = new LinuxFiles(new GSettings(new FakeGSettings().Run), Record);
        Assert.True(files.MoveToRecycleBin("/tmp/a b.png"));
        Assert.Equal(["gio trash /tmp/a b.png"], calls);
    }

    [Fact]
    public void RevealAsksTheFileManagerToSelectTheItem()
    {
        Linux.Only();
        new LinuxFiles(new GSettings(new FakeGSettings().Run), Record).Reveal("/home/sam/Sam's shots/a b.png");
        var call = Assert.Single(calls);
        Assert.Contains("org.freedesktop.FileManager1.ShowItems ['file:///home/sam/Sam\\'s%20shots/a%20b.png'] ''", call);
    }

    [Fact]
    public void WithoutAFileManagerServiceRevealOpensTheFolder()
    {
        Linux.Only();
        fileManagerExit = 1;
        new LinuxFiles(new GSettings(new FakeGSettings().Run), Record).Reveal("/home/sam/shots/a.png");
        Assert.Equal("gio open /home/sam/shots", calls[^1]);
    }

    [Fact]
    public void TheWallpaperIsTheOneForTheCurrentStyle()
    {
        var (light, dark) = (Picture(30), Picture(60));
        var fake = new FakeGSettings();
        fake.Values["org.gnome.desktop.background|picture-uri"] = $"'{new Uri(light).AbsoluteUri}'";
        fake.Values["org.gnome.desktop.background|picture-uri-dark"] = $"'{new Uri(dark).AbsoluteUri}'";
        fake.Values["org.gnome.desktop.interface|color-scheme"] = "'prefer-dark'";
        using var wallpaper = new LinuxFiles(new GSettings(fake.Run)).Wallpaper();
        Assert.Equal(60, wallpaper!.Width);

        fake.Values["org.gnome.desktop.interface|color-scheme"] = "'default'";
        using var lightOne = new LinuxFiles(new GSettings(fake.Run)).Wallpaper();
        Assert.Equal(30, lightOne!.Width);
    }

    [Fact]
    public void ADarkStyleWithNoDarkPictureUsesTheLightOne()
    {
        var fake = new FakeGSettings();
        fake.Values["org.gnome.desktop.background|picture-uri"] = $"'{new Uri(Picture(30)).AbsoluteUri}'";
        fake.Values["org.gnome.desktop.background|picture-uri-dark"] = "''";
        fake.Values["org.gnome.desktop.interface|color-scheme"] = "'prefer-dark'";
        using var wallpaper = new LinuxFiles(new GSettings(fake.Run)).Wallpaper();
        Assert.Equal(30, wallpaper!.Width);
    }

    [Fact]
    public void ASlideshowOrMissingFileIsNoPicture()
    {
        var slideshow = Path.Combine(Directory.CreateTempSubdirectory().FullName, "shows.xml");
        File.WriteAllText(slideshow, "<background></background>");
        var fake = new FakeGSettings();
        fake.Values["org.gnome.desktop.background|picture-uri"] = $"'{new Uri(slideshow).AbsoluteUri}'";
        Assert.Null(new LinuxFiles(new GSettings(fake.Run)).Wallpaper());
        fake.Values["org.gnome.desktop.background|picture-uri"] = "'file:///nowhere/at/all.png'";
        Assert.Null(new LinuxFiles(new GSettings(fake.Run)).Wallpaper());
    }

    [Fact]
    public void ReducedMotionFollowsGnomesAnimationsSetting()
    {
        var fake = new FakeGSettings();
        fake.Values["org.gnome.desktop.interface|enable-animations"] = "false";
        Assert.True(new LinuxFiles(new GSettings(fake.Run)).ReduceMotion);
        fake.Values["org.gnome.desktop.interface|enable-animations"] = "true";
        Assert.False(new LinuxFiles(new GSettings(fake.Run)).ReduceMotion);
    }

    private static string Picture(int width)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "picture one.png");
        using var surface = SKSurface.Create(new SKImageInfo(width, 10));
        using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }
}
