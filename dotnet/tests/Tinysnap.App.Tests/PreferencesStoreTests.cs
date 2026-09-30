using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Tinysnap.App.Editing;
using Tinysnap.Core;
using Tinysnap.Dev;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class PreferencesStoreTests
{
    private static string TemporaryFile() => Path.Combine(Path.GetTempPath(), $"tinysnap-prefs-{Guid.NewGuid()}", "preferences.json");

    [Fact]
    public void ChangesAreWrittenAndAnnounced()
    {
        var path = TemporaryFile();
        var store = new PreferencesStore(path);
        Preferences? announced = null;
        store.Changed += changed => announced = changed;
        store.Update(p => p with { DelaySeconds = 7 });
        Assert.Equal(7, store.Current.DelaySeconds);
        Assert.Equal(7, announced!.DelaySeconds);
        Assert.Equal(7, Preferences.Load(path).DelaySeconds);
    }

    [Fact]
    public void ADamagedFileReadsAsDefaults()
    {
        var path = TemporaryFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not json");
        Assert.Equal(Preferences.Defaults, new PreferencesStore(path).Current);
    }

    [Fact]
    public void RememberedStylesKeepOtherSettings()
    {
        var path = TemporaryFile();
        var store = new PreferencesStore(path);
        // Written by hand while the app runs.
        (Preferences.Defaults with { SaveFolder = "~/Elsewhere" }).Save(path);
        var thick = Tool.Arrow.DefaultStyle() with { Size = StyleSize.Large };
        store.RememberStyles(new Dictionary<Tool, Style> { [Tool.Arrow] = thick }, "#007AFF");
        var written = Preferences.Load(path);
        Assert.Equal("~/Elsewhere", written.SaveFolder);
        Assert.Equal(StyleSize.Large, written.Styles[Tool.Arrow].Size);
        Assert.Equal("#007AFF", written.ColorHex);
    }

    [AvaloniaFact]
    public void ACommittedStyleIsRemembered()
    {
        IReadOnlyDictionary<Tool, Style>? remembered = null;
        var services = Make() with { RememberStyles = (styles, _) => remembered = styles };
        var editor = Editor(services);
        Press(editor, Key.OemCloseBrackets, symbol: "]");
        Assert.Equal(Tool.Arrow.DefaultStyle().Size.Thicker(), remembered![Tool.Arrow].Size);
    }
}
