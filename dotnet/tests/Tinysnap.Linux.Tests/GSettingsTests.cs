namespace Tinysnap.Linux.Tests;

public class GSettingsTests
{
    [Theory]
    [InlineData("@as []", new string[0])]
    [InlineData("['Print']", new[] { "Print" })]
    [InlineData("['<Shift><Control>2', '<Super>a']", new[] { "<Shift><Control>2", "<Super>a" })]
    [InlineData(@"['it\'s']", new[] { "it's" })]
    [InlineData("\"file:///home/sam/Sam's.png\"", new[] { "file:///home/sam/Sam's.png" })]
    [InlineData("['a', \"it's\", 'b']", new[] { "a", "it's", "b" })]
    public void ReadsStringArraysAsGsettingsPrintsThem(string text, string[] expected) =>
        Assert.Equal(expected, GSettings.ParseStrings(text));

    [Fact]
    public void WritesStringArraysGsettingsReadsBack()
    {
        var fake = new FakeGSettings();
        var settings = new GSettings(fake.Run);
        Assert.True(settings.SetStrings("org.x", "list", ["Print", "it's"]));
        Assert.Equal(["Print", "it's"], settings.GetStrings("org.x", "list"));
        Assert.Equal("'Tinysnap: Capture Area'", GSettings.Quote("Tinysnap: Capture Area"));
    }

    [Fact]
    public void AMissingToolReadsAsNothingRatherThanThrowing()
    {
        var settings = new GSettings((_, _) => (-1, ""));
        Assert.Null(settings.Get("org.x", "key"));
        Assert.Empty(settings.GetStrings("org.x", "list"));
    }

    [Fact]
    public void ListsEveryValueOfASchema()
    {
        var fake = new FakeGSettings();
        fake.Values["org.gnome.desktop.wm.keybindings|close"] = "['<Alt>F4']";
        fake.Values["org.gnome.desktop.wm.keybindings|minimize"] = "['<Super>h']";
        Assert.Equal(["['<Alt>F4']", "['<Super>h']"], new GSettings(fake.Run).ListValues("org.gnome.desktop.wm.keybindings").Order());
    }
}
