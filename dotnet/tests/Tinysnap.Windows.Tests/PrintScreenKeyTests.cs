using Microsoft.Win32;

namespace Tinysnap.Windows.Tests;

/// <summary>Up to 1.4.4 Tinysnap turned the Snipping Tool's Print Screen setting off while it held the
/// key, keeping what it held in its own key. A copy killed first left it off; this puts it back.</summary>
public class PrintScreenKeyTests : IDisposable
{
    private const string Setting = "PrintScreenKeyForSnippingEnabled";
    private readonly string keyboard = $@"Software\TinysnapTest{Guid.NewGuid():N}\Keyboard";
    private string Own => Path.Combine(Path.GetDirectoryName(keyboard)!, "Tinysnap");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(Path.GetDirectoryName(keyboard)!, throwOnMissingSubKey: false);

    private object? Value()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyboard);
        return key?.GetValue(Setting);
    }

    private void Set(int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyboard);
        key.SetValue(Setting, value, RegistryValueKind.DWord);
    }

    /// <summary>As an earlier copy left it: the setting off, and what it held kept in Tinysnap's key.</summary>
    private void LeftOffByAnEarlierCopy(object before)
    {
        Set(0);
        using var own = Registry.CurrentUser.CreateSubKey(Own);
        own.SetValue("PrintScreenKeyBefore", before);
    }

    [Fact]
    public void ASettingThatWasOnIsPutBackOn()
    {
        LeftOffByAnEarlierCopy(1);
        new PrintScreenKey(keyboard, Own).GiveBack();
        Assert.Equal(1, Value());
    }

    [Fact]
    public void ASettingThatWasNeverSetIsLeftUnsetAgain()
    {
        LeftOffByAnEarlierCopy("unset");
        new PrintScreenKey(keyboard, Own).GiveBack();
        Assert.Null(Value());
    }

    /// <summary>Once put back, it is the person's: a later start leaves it as they set it.</summary>
    [Fact]
    public void ASettingIsPutBackOnceThenLeftAlone()
    {
        LeftOffByAnEarlierCopy(1);
        new PrintScreenKey(keyboard, Own).GiveBack();
        Set(0);
        new PrintScreenKey(keyboard, Own).GiveBack();
        Assert.Equal(0, Value());
    }
}
