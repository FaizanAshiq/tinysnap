namespace Tinysnap.Linux.Tests;

public class TesseractLanguagesTests
{
    private static readonly HashSet<string> Bundled =
        ["eng", "fra", "ita", "deu", "spa", "por", "chi_sim", "chi_tra", "kor", "jpn", "rus", "ukr", "tha", "vie", "ara"];

    [Theory]
    [InlineData(null, "en_GB.UTF-8", "eng")]
    [InlineData(null, "fr_FR.UTF-8", "eng+fra")]
    [InlineData("fr:de", "en_US.UTF-8", "eng+fra+deu")]
    [InlineData("", "de_DE.UTF-8", "eng+deu")]
    [InlineData(null, "zh_TW.UTF-8", "eng+chi_tra")]
    [InlineData(null, "zh_CN.UTF-8", "eng+chi_sim")]
    [InlineData(null, "zh_HK", "eng+chi_tra")]
    [InlineData(null, "pt_BR.UTF-8", "eng+por")]
    [InlineData(null, "ja_JP.UTF-8", "eng+jpn")]
    [InlineData(null, "C", "eng")]
    [InlineData(null, "C.UTF-8", "eng")]
    [InlineData(null, null, "eng")]
    [InlineData("uk:ru", "uk_UA.UTF-8", "eng+ukr+rus")]
    public void EnglishAndThePersonsOwnLanguages(string? language, string? lang, string expected) =>
        Assert.Equal(expected, TesseractLanguages.For(language, lang, Bundled));

    [Fact]
    public void ALanguageWithNoDataIsLeftOut() =>
        Assert.Equal("eng", TesseractLanguages.For("nl", "nl_NL.UTF-8", Bundled));

    [Fact]
    public void ASystemLanguageIsUsedToo() =>
        Assert.Equal("eng+nld", TesseractLanguages.For(null, "nl_NL.UTF-8", new HashSet<string>(Bundled) { "nld" }));

    [Fact]
    public void WithoutEnglishTheFirstLanguageFound() =>
        Assert.Equal("fra", TesseractLanguages.For(null, "fr_FR.UTF-8", new HashSet<string> { "fra" }));

    [Fact]
    public void NoLanguageAtAllIsNothing() =>
        Assert.Equal("", TesseractLanguages.For(null, "fr_FR.UTF-8", new HashSet<string>()));

    [Fact]
    public void TheFolderHoldsBundledAndSystemLanguagesTheBundledWinning()
    {
        string Folder(params string[] names)
        {
            var folder = Directory.CreateTempSubdirectory().FullName;
            foreach (var name in names) File.WriteAllText(Path.Combine(folder, $"{name}.traineddata"), folder);
            return folder;
        }
        var bundled = Folder("eng", "fra");
        var system = Folder("eng", "nld");
        var cache = Path.Combine(Directory.CreateTempSubdirectory().FullName, "tessdata");
        var merged = TesseractLanguages.Folder(bundled, system, cache);
        Assert.Equal(["eng", "fra", "nld"], TesseractLanguages.Available(merged).Order());
        Assert.Equal(bundled, File.ReadAllText(Path.Combine(merged, "eng.traineddata")));
        Assert.Equal(system, File.ReadAllText(Path.Combine(merged, "nld.traineddata")));
    }

    [Fact]
    public void WithNothingExtraOnTheSystemTheBundledFolderIsUsedAsItIs()
    {
        var bundled = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(bundled, "eng.traineddata"), "");
        Assert.Equal(bundled, TesseractLanguages.Folder(bundled, "/no/such/folder", Path.Combine(bundled, "cache")));
    }
}
