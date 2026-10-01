using Microsoft.Win32;

namespace Tinysnap.Windows.Tests;

/// <summary>Windows 11 hands Print Screen to the Snipping Tool while its setting is on, or was
/// never set. Tinysnap turns it off while it holds the key and puts it back exactly as it was.</summary>
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

    [Fact]
    public void ASettingThatWasOnIsTurnedOffThenBackOn()
    {
        Set(1);
        var printScreen = new PrintScreenKey(keyboard, Own);
        printScreen.Take();
        Assert.Equal(0, Value());
        printScreen.GiveBack();
        Assert.Equal(1, Value());
    }

    [Fact]
    public void ASettingNeverSetIsTurnedOffThenLeftUnsetAgain()
    {
        Registry.CurrentUser.CreateSubKey(keyboard).Dispose();
        var printScreen = new PrintScreenKey(keyboard, Own);
        printScreen.Take();
        Assert.Equal(0, Value());
        printScreen.GiveBack();
        Assert.Null(Value());
    }

    [Fact]
    public void ASettingLeftOffByACopyThatWasKilledIsStillPutBack()
    {
        Set(1);
        new PrintScreenKey(keyboard, Own).Take();
        // Killed: never gave it back. The next copy finds it off, and knows it was on.
        var next = new PrintScreenKey(keyboard, Own);
        next.Take();
        Assert.Equal(0, Value());
        next.GiveBack();
        Assert.Equal(1, Value());
    }

    [Fact]
    public void ASettingAlreadyOffIsLeftAlone()
    {
        Set(0);
        var printScreen = new PrintScreenKey(keyboard, Own);
        printScreen.Take();
        printScreen.GiveBack();
        Assert.Equal(0, Value());
    }
}
