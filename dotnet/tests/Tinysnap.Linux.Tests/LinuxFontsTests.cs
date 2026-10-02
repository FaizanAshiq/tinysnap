namespace Tinysnap.Linux.Tests;

public class LinuxFontsTests
{
    private static readonly HashSet<string> Installed = new(StringComparer.OrdinalIgnoreCase)
        { "C059", "Nimbus Roman", "DejaVu Sans", "Ubuntu Sans", "Noto Sans" };

    [Fact]
    public void GnomesOwnInterfaceFontComesFirst() =>
        Assert.Equal("Ubuntu Sans", LinuxFonts.Choose("'Ubuntu Sans 11'", "DejaVu Sans", Installed));

    [Fact]
    public void AStyleNamedInGnomesFontIsLeftOff() =>
        Assert.Equal("Noto Sans", LinuxFonts.Choose("'Noto Sans Medium 10'", null, Installed));

    [Fact]
    public void AGnomeFontThatIsNotInstalledGivesWayToSansSerif() =>
        Assert.Equal("DejaVu Sans", LinuxFonts.Choose("'Cantarell 11'", "DejaVu Sans", Installed));

    [Fact]
    public void WithoutEitherACommonSansFaceIsChosenNeverTheFirstInstalled()
    {
        // fontconfig could resolve no alias in a C locale, and the first family was C059, a
        // serif face with no Medium: the first editor crashed.
        Assert.Equal("Ubuntu Sans", LinuxFonts.Choose(null, null, Installed));
        Assert.Equal("DejaVu Sans", LinuxFonts.Choose(null, "", new HashSet<string> { "C059", "DejaVu Sans" }));
    }

    [Fact]
    public void NothingKnownLeavesTheChoiceToAvalonia() =>
        Assert.Null(LinuxFonts.Choose(null, null, new HashSet<string> { "C059", "Nimbus Roman" }));
}
