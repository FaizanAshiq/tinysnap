using Microsoft.Win32;

namespace Tinysnap.Windows.Tests;

/// <summary>Windows keeps one key per notification icon, naming the exe that shows it; its
/// IsPromoted value says whether the icon sits in the taskbar or behind the arrow.</summary>
public class TrayPromotionTests : IDisposable
{
    private readonly string root = $@"Software\TinysnapTest{Guid.NewGuid():N}\NotifyIconSettings";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(Path.GetDirectoryName(root)!, throwOnMissingSubKey: false);

    private void Icon(string id, string exe, int? promoted = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{root}\{id}");
        key.SetValue("ExecutablePath", exe);
        if (promoted is { } value) key.SetValue("IsPromoted", value, RegistryValueKind.DWord);
    }

    private object? Promoted(string id)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{root}\{id}");
        return key?.GetValue("IsPromoted");
    }

    [Fact]
    public void TinysnapsIconIsShownInTheTaskbar()
    {
        Icon("1", @"C:\Other\other.exe");
        Icon("2", @"C:\Users\sam\AppData\Local\Tinysnap\current\Tinysnap.exe");
        Assert.True(TrayPromotion.Promote(@"C:\Users\sam\AppData\Local\Tinysnap\current\Tinysnap.exe", root));
        Assert.Equal(1, Promoted("2"));
        Assert.Null(Promoted("1"));
    }

    [Fact]
    public void AnIconThePersonHidAgainStaysHidden()
    {
        Icon("2", @"C:\Tinysnap\Tinysnap.exe", promoted: 0);
        Assert.True(TrayPromotion.Promote(@"C:\Tinysnap\Tinysnap.exe", root));
        Assert.Equal(0, Promoted("2"));
    }

    [Fact]
    public void NoEntryYetMeansTryAgainLater()
    {
        Assert.False(TrayPromotion.Promote(@"C:\Tinysnap\Tinysnap.exe", root));
    }
}
