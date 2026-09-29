using Microsoft.Win32;

namespace Tinysnap.Windows.Tests;

/// <summary>Run on a real Windows desktop, in CI.</summary>
public class FilesTests
{
    [Fact]
    public void AFolderGoesToTheRecycleBin()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tinysnap-recycle-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "edits.json"), "{}");
        Assert.True(new Win32Files().MoveToRecycleBin(folder));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void AMissingFolderIsNotMoved()
    {
        Assert.False(new Win32Files().MoveToRecycleBin(Path.Combine(Path.GetTempPath(), $"tinysnap-missing-{Guid.NewGuid()}")));
    }

    [Fact]
    public void StartAtLoginWritesAndRemovesTheRunValue()
    {
        var name = $"TinysnapTest{Guid.NewGuid():N}";
        var startup = new Win32Startup(name);
        try
        {
            Assert.False(startup.IsEnabled);
            Assert.True(startup.SetEnabled(true));
            Assert.True(startup.IsEnabled);
            using (var run = Registry.CurrentUser.OpenSubKey(Win32Startup.RunKey))
                Assert.Equal($"\"{Environment.ProcessPath}\"", run!.GetValue(name));
            Assert.True(startup.SetEnabled(false));
            Assert.False(startup.IsEnabled);
        }
        finally
        {
            startup.SetEnabled(false);
        }
    }
}
