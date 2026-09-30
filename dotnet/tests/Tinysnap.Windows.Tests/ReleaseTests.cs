using System.Diagnostics;

namespace Tinysnap.Windows.Tests;

/// <summary>What Explorer and Settings show for the exe. Run on Windows, in CI.</summary>
public class ReleaseTests
{
    [Fact]
    public void TheExeCarriesTheMacVersion()
    {
        var info = FileVersionInfo.GetVersionInfo(typeof(WindowsPlatform).Assembly.Location);
        Assert.StartsWith("1.1.0", info.ProductVersion);
        Assert.Equal("Tinysnap", info.ProductName);
        Assert.Equal("Tinysnap.dll", Path.GetFileName(typeof(WindowsPlatform).Assembly.Location));
    }
}
