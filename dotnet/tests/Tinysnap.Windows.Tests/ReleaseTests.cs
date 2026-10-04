using System.Diagnostics;

namespace Tinysnap.Windows.Tests;

/// <summary>What Explorer and Settings show for the exe. Run on Windows, in CI.</summary>
public class ReleaseTests
{
    [Fact]
    public void TheExeCarriesTheMacVersion()
    {
        var info = FileVersionInfo.GetVersionInfo(typeof(WindowsPlatform).Assembly.Location);
        Assert.StartsWith("1.4.0", info.ProductVersion);
        Assert.Equal("Tinysnap", info.ProductName);
        Assert.Equal("Tinysnap.dll", Path.GetFileName(typeof(WindowsPlatform).Assembly.Location));
    }

    [Fact]
    public void UninstallClearsStartAtLogin()
    {
        var startup = new Win32Startup($"TinysnapTest{Guid.NewGuid():N}");
        try
        {
            startup.SetEnabled(true);
            Program.Uninstalling(startup, new Win32FileTypes($"TinysnapTest{Guid.NewGuid():N}", ".tinysnaptest"));
            Assert.False(startup.IsEnabled);
        }
        finally
        {
            startup.SetEnabled(false);
        }
    }

    [Fact]
    public void UninstallGivesPrintScreenBackToTheSnippingTool()
    {
        var root = $@"Software\TinysnapTest{Guid.NewGuid():N}";
        try
        {
            using (var keyboard = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"{root}\Keyboard"))
                keyboard.SetValue("PrintScreenKeyForSnippingEnabled", 1, Microsoft.Win32.RegistryValueKind.DWord);
            var printScreen = new PrintScreenKey($@"{root}\Keyboard", $@"{root}\Tinysnap");
            printScreen.Take();
            // Killed while holding the key, then uninstalled.
            Program.Uninstalling(new Win32Startup($"TinysnapTest{Guid.NewGuid():N}"),
                                 new Win32FileTypes($"TinysnapTest{Guid.NewGuid():N}", ".tinysnaptest"),
                                 new PrintScreenKey($@"{root}\Keyboard", $@"{root}\Tinysnap"));
            using var after = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{root}\Keyboard");
            Assert.Equal(1, after!.GetValue("PrintScreenKeyForSnippingEnabled"));
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void InstallingPutsTinysnapInOpenWithAndUninstallingTakesItOut()
    {
        var types = new Win32FileTypes($"TinysnapTest{Guid.NewGuid():N}", ".tinysnaptest");
        try
        {
            Program.Installed(types);
            Assert.True(types.IsRegisteredFor(".tinysnaptest"));
            Program.Uninstalling(new Win32Startup($"TinysnapTest{Guid.NewGuid():N}"), types);
            Assert.False(types.IsRegisteredFor(".tinysnaptest"));
        }
        finally
        {
            types.Unregister();
        }
    }

    [Fact]
    public void TheFirstCopyClosesFromAnyThreadAndFreesItsPlace()
    {
        var name = $"TinysnapTest{Guid.NewGuid():N}";
        var first = new SingleInstance(name);
        Exception? failed = null;
        var elsewhere = new Thread(() =>
        {
            try { first.Dispose(); }
            catch (Exception error) { failed = error; }
        });
        elsewhere.Start();
        elsewhere.Join();
        Assert.Null(failed);
        using var next = new SingleInstance(name);
        Assert.True(next.IsFirst);
    }

    [Fact]
    public async Task ASecondCopyHandsItsFilesToTheFirst()
    {
        var name = $"TinysnapTest{Guid.NewGuid():N}";
        using var first = new SingleInstance(name);
        var handed = new TaskCompletionSource<IReadOnlyList<string>>();
        first.Reopened += files => handed.TrySetResult(files);
        using var second = new SingleInstance(name);
        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
        second.AskFirstToReopen([@"C:\Pictures\Receipt.png"]);
        var files = await handed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal([@"C:\Pictures\Receipt.png"], files);
    }
}
