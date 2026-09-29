namespace Tinysnap.Core.Tests;

public class PreferencesTests
{
    private static string TemporaryFile(string? json = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tinysnap-tests-{Guid.NewGuid()}.json");
        if (json is not null) File.WriteAllText(path, json);
        return path;
    }

    private static Preferences Reloaded(Preferences preferences)
    {
        var path = TemporaryFile();
        preferences.Save(path);
        return Preferences.Load(path);
    }

    [Fact]
    public void AMissingFileGivesDefaults()
    {
        Assert.Equal(Preferences.Defaults, Preferences.Load(TemporaryFile()));
    }

    [Fact]
    public void APartialFileKeepsTheRestAtDefaults()
    {
        var preferences = Preferences.Load(TemporaryFile("""{"delaySeconds": 5}"""));
        Assert.Equal(5, preferences.DelaySeconds);
        Assert.Equal(HotKeys.Defaults, preferences.HotKeys);
    }

    [Fact]
    public void NullMeansNoHotkeyAndMissingMeansTheDefault()
    {
        var preferences = Preferences.Load(TemporaryFile("""{"hotkeys": {"area": null}}"""));
        Assert.Null(preferences.HotKeys.Area);
        Assert.Equal(HotKeys.Defaults.Fullscreen, preferences.HotKeys.Fullscreen);
    }

    [Fact]
    public void ScanQRCodeHasNoHotkeyUntilOneIsSet()
    {
        Assert.Null(HotKeys.Defaults.Qr);
        var actions = Enum.GetValues<HotKeyAction>().ToList();
        Assert.Equal(actions.IndexOf(HotKeyAction.Text) + 1, actions.IndexOf(HotKeyAction.Qr));
        // A file from before Scan QR Code existed leaves it unset.
        Assert.Null(Preferences.Load(TemporaryFile("""{"hotkeys": {"area": null}}""")).HotKeys.Qr);
        var qr = new HotKeyBinding(0x52, [ModifierKey.Control, ModifierKey.Shift]);
        var preferences = Preferences.Defaults with { HotKeys = HotKeys.Defaults with { Qr = qr } };
        Assert.Equal(qr, Reloaded(preferences).HotKeys.Qr);
    }

    [Fact]
    public void AClearedHotkeyStaysClearedAfterSaving()
    {
        var preferences = Preferences.Defaults with { HotKeys = HotKeys.Defaults with { Area = null } };
        Assert.Null(Reloaded(preferences).HotKeys.Area);
    }

    [Fact]
    public void BadValuesFallBackOneByOne()
    {
        var json = """
        {"exportScale": "3x", "delaySeconds": -5, "saveFolder": 7,
         "toolStyles": {"arrow": {"colorHex": "#007AFF", "size": "huge"}}}
        """;
        var preferences = Preferences.Load(TemporaryFile(json));
        Assert.Equal(ExportScale.Native, preferences.ExportScale);
        Assert.Equal(1, preferences.DelaySeconds);
        Assert.Equal("~/Pictures/Screenshots", preferences.SaveFolder);
        Assert.Equal(new Style("#007AFF", StyleSize.Medium), preferences.ToolStyles["arrow"]);
    }

    [Fact]
    public void ExpandsTheSaveFolderFromHome()
    {
        // Windows keeps screenshots in Pictures\Screenshots, so that is the default there.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, "Pictures", "Screenshots"), Preferences.Defaults.SaveFolderPath);
    }

    [Fact]
    public void StylesMapBackToToolsAndSkipUnknownOnes()
    {
        var style = new Style("#34C759");
        var preferences = Preferences.Defaults with
        {
            ToolStyles = new Dictionary<string, Style> { ["arrow"] = style, ["nonsense"] = style },
        };
        Assert.Equal(new Dictionary<Tool, Style> { [Tool.Arrow] = style }, preferences.Styles);
    }

    [Fact]
    public void FindsTheActionAlreadyUsingACombinationInAnyModifierOrder()
    {
        var same = new HotKeyBinding(0x32, [ModifierKey.Shift, ModifierKey.Control]);
        Assert.Equal(HotKeyAction.Area, HotKeys.Defaults.ActionUsing(same));
        Assert.Null(HotKeys.Defaults.ActionUsing(new HotKeyBinding(0x32, [ModifierKey.Control])));
    }

    [Fact]
    public void OneRememberedColourStartsRedAndABadOneFallsBack()
    {
        Assert.Equal(Palette.Red, Preferences.Defaults.ColorHex);
        Assert.Equal(Palette.Red, Preferences.Load(TemporaryFile("""{"colorHex": "blue"}""")).ColorHex);
        Assert.Equal("#007AFF", Reloaded(Preferences.Defaults with { ColorHex = "#007AFF" }).ColorHex);
    }

    [Fact]
    public void TheTrayIconShowsByDefault()
    {
        // Windows has one icon to hide, in the notification area, where the Mac has the
        // menu bar icon and the Dock icon.
        Assert.True(Preferences.Defaults.ShowTrayIcon);
    }

    [Fact]
    public void TheTrayIconCanBeTurnedOffAndABadValueFallsBack()
    {
        Assert.False(Preferences.Load(TemporaryFile("""{"showTrayIcon": false}""")).ShowTrayIcon);
        Assert.True(Preferences.Load(TemporaryFile("""{"showTrayIcon": "no"}""")).ShowTrayIcon);
        Assert.False(Reloaded(Preferences.Defaults with { ShowTrayIcon = false }).ShowTrayIcon);
    }

    [Fact]
    public void CaptureTextDefaultsToCtrlShiftOAndOpenLibraryHasNoHotkey()
    {
        Assert.Equal(new HotKeyBinding(0x4F, [ModifierKey.Control, ModifierKey.Shift]), HotKeys.Defaults.Text);
        Assert.Equal("Ctrl+Shift+O", HotKeys.Defaults.Text?.DisplayString);
        Assert.Null(HotKeys.Defaults.Library);
    }

    [Fact]
    public void TheMenuListsCaptureTextThenScanQRCodeAndOpenLibraryLast()
    {
        Assert.Equal(
        [
            "Capture Area", "Capture Fullscreen", "Capture Text", "Scan QR Code", "Repeat Last Area", "Delayed Capture",
            "Open Library",
        ], Enum.GetValues<HotKeyAction>().Select(action => action.Title()));
    }

    [Fact]
    public void AFileFromBeforeTheNewHotkeysGetsTheirDefaults()
    {
        var json = """{"hotkeys": {"area": null, "fullscreen": null, "repeatArea": null, "delayed": null}}""";
        var preferences = Preferences.Load(TemporaryFile(json));
        Assert.Null(preferences.HotKeys.Area);
        Assert.Equal(HotKeys.Defaults.Text, preferences.HotKeys.Text);
        Assert.Null(preferences.HotKeys.Library);
    }

    [Fact]
    public void TheNewHotkeysSurviveSavingAndAClearedOneStaysCleared()
    {
        var library = new HotKeyBinding(0x4C, [ModifierKey.Control, ModifierKey.Alt]);
        var saved = Preferences.Defaults with { HotKeys = HotKeys.Defaults with { Text = null, Library = library } };
        var loaded = Reloaded(saved);
        Assert.Null(loaded.HotKeys.Text);
        Assert.Equal(library, loaded.HotKeys.Library);
        Assert.Equal(HotKeyAction.Library,
                     loaded.HotKeys.ActionUsing(new HotKeyBinding(0x4C, [ModifierKey.Alt, ModifierKey.Control])));
    }

    [Fact]
    public void ACaptureOpensTheEditorAndTheLibraryKeepsItByDefault()
    {
        Assert.Equal(AfterCapture.Editor, Preferences.Defaults.AfterCapture);
        Assert.True(Preferences.Defaults.KeepLibrary);
    }

    [Fact]
    public void AfterCaptureAndKeepLibraryFallBackAloneAndSurviveSaving()
    {
        var bad = Preferences.Load(TemporaryFile("""{"afterCapture": "popup", "keepLibrary": "yes", "delaySeconds": 5}"""));
        Assert.Equal(AfterCapture.Editor, bad.AfterCapture);
        Assert.True(bad.KeepLibrary);
        Assert.Equal(5, bad.DelaySeconds);

        var loaded = Reloaded(Preferences.Defaults with { AfterCapture = AfterCapture.Thumbnail, KeepLibrary = false });
        Assert.Equal(AfterCapture.Thumbnail, loaded.AfterCapture);
        Assert.False(loaded.KeepLibrary);
    }

    [Fact]
    public void TheLastBackdropSettingsAreRememberedAndFallBackAlone()
    {
        Assert.Equal(Backdrop.Defaults, Preferences.Defaults.Backdrop);
        var bad = Preferences.Load(TemporaryFile("""{"backdrop": {"fill": "clear", "shadow": 3}, "delaySeconds": 5}"""));
        Assert.Equal(BackdropFill.Clear, bad.Backdrop.Fill);
        Assert.Equal(Backdrop.Defaults.Shadow, bad.Backdrop.Shadow);
        Assert.Equal(5, bad.DelaySeconds);
    }

    [Fact]
    public void TheMeasureToolComesBackAsItWasLeftAndFallsBackAlone()
    {
        Assert.Equal(MeasureSettings.Defaults, Preferences.Defaults.Measure);
        var left = Preferences.Load(TemporaryFile("""{"measure": {"down": true, "edgeContrast": 0.02, "guideSeen": true}}"""));
        Assert.Equal(new MeasureSettings(true, true, 0.02, true), left.Measure);
        var bad = Preferences.Load(TemporaryFile("""{"measure": 5, "delaySeconds": 4}"""));
        Assert.Equal(MeasureSettings.Defaults, bad.Measure);
        Assert.Equal(4, bad.DelaySeconds);
    }

    [Fact]
    public void TheRememberedBackdropNeverHoldsOnToAWallpaper()
    {
        var backdrop = Backdrop.Defaults with
        {
            Fill = BackdropFill.Wallpaper,
            Wallpaper = new BackdropWallpaper(Guid.NewGuid(), new PastedImage(Fixture.CaptureImage(4, 4))),
        };
        var preferences = Preferences.Defaults with { Backdrop = backdrop };
        // Kept, it would stand in for the desktop picture of another screen, or of a
        // desktop changed since.
        Assert.Null(preferences.Backdrop.Wallpaper);
        Assert.Equal(BackdropFill.Wallpaper, preferences.Backdrop.Fill);
        Assert.Null(new Preferences { Backdrop = backdrop }.Backdrop.Wallpaper);
    }

    [Fact]
    public void ABindingMadeWithoutAModifierListStillPrintsAndSaves()
    {
        // A default array threw the moment it was written or listed.
        var binding = new HotKeyBinding(0x32, default);
        Assert.Empty(binding.Modifiers);
        Assert.Equal("2", binding.DisplayString);
        Assert.Equal(binding, HotKeyBinding.FromJson(binding.ToJson()));
    }

    [Fact]
    public void PrintsHotkeysTheWayWindowsDoes()
    {
        Assert.Equal("Ctrl+Shift+2", HotKeys.Defaults.Area?.DisplayString);
    }
}
