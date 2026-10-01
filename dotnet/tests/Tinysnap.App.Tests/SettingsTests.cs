using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Tinysnap.App.Settings;
using Tinysnap.Core;
using Tinysnap.Dev;
using static Tinysnap.App.Tests.TestServices;

namespace Tinysnap.App.Tests;

public class SettingsTests
{
    private static (AppSetup Setup, SettingsWindow Window) Open(Preferences? preferences = null)
    {
        var setup = Launch(preferences);
        var window = setup.Controller.ShowSettings();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (setup, window);
    }

    private static HotKeyBinding Binding(uint key, params ModifierKey[] modifiers) => new(key, [.. modifiers]);

    /// <summary>Clicks the field, then presses <paramref name="key"/> with <paramref name="modifiers"/>.</summary>
    private static void Record(SettingsWindow window, HotKeyRecorder recorder, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        recorder.StartRecording();
        recorder.Focus();
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }

    [AvaloniaFact]
    public void TheRecorderTakesACombination()
    {
        var (setup, window) = Open();
        Record(window, window.Recorders[HotKeyAction.Area], Key.J, RawInputModifiers.Control | RawInputModifiers.Shift);
        var expected = Binding(0x4A, ModifierKey.Control, ModifierKey.Shift);
        Assert.Equal(expected, setup.Controller.Preferences.Current.HotKeys.Area);
        Assert.Equal(expected, Preferences.Load(setup.Controller.Preferences.Path).HotKeys.Area);
        Assert.Equal("Ctrl+Shift+J", window.Recorders[HotKeyAction.Area].Text);
    }

    [AvaloniaFact]
    public void DeleteClearsAHotkey()
    {
        var (setup, window) = Open();
        Record(window, window.Recorders[HotKeyAction.Area], Key.Delete);
        Assert.Null(setup.Controller.Preferences.Current.HotKeys.Area);
        Assert.Equal("None", window.Recorders[HotKeyAction.Area].Text);
    }

    [AvaloniaFact]
    public void AKeyWithoutAModifierIsRefused()
    {
        var (setup, window) = Open();
        var recorder = window.Recorders[HotKeyAction.Area];
        Record(window, recorder, Key.J);
        Assert.True(recorder.IsRecording);
        Assert.Equal(HotKeys.Defaults.Area, setup.Controller.Preferences.Current.HotKeys.Area);
    }

    [AvaloniaFact]
    public void PrintScreenNeedsNoModifierAndIsTakenOnItsRelease()
    {
        var (setup, window) = Open(Preferences.Defaults with { HotKeys = HotKeys.Defaults with { Area = null } });
        var recorder = window.Recorders[HotKeyAction.Fullscreen];
        recorder.StartRecording();
        recorder.Focus();
        // Windows hands apps Print Screen on its release only.
        window.KeyRelease(Key.PrintScreen, RawInputModifiers.None, PhysicalKey.PrintScreen, null);
        Assert.Equal(Binding(0x2C), setup.Controller.Preferences.Current.HotKeys.Fullscreen);
        Assert.Equal("Print Screen", recorder.Text);
    }

    [AvaloniaFact]
    public void ACombinationAnotherActionUsesIsRefused()
    {
        var (setup, window) = Open();
        Record(window, window.Recorders[HotKeyAction.Fullscreen], Key.O, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal(HotKeys.Defaults.Fullscreen, setup.Controller.Preferences.Current.HotKeys.Fullscreen);
        Assert.False(window.Recorders[HotKeyAction.Fullscreen].IsRecording);
    }

    [AvaloniaFact]
    public void HotkeysPauseWhileAFieldRecords()
    {
        var (setup, window) = Open();
        var hotkeys = (FakeHotkeys)setup.Platform.Hotkeys;
        Assert.NotEmpty(hotkeys.Registered);
        var recorder = window.Recorders[HotKeyAction.Area];
        recorder.StartRecording();
        Assert.Empty(hotkeys.Registered);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(recorder.IsRecording);
        Assert.Equal(HotKeys.Defaults.Area, hotkeys.Registered[HotKeyAction.Area]);
    }

    [AvaloniaFact]
    public void AHotkeyAnotherAppHoldsIsMarkedTaken()
    {
        var setup = Launch();
        var hotkeys = (FakeHotkeys)setup.Platform.Hotkeys;
        hotkeys.HeldElsewhere.Add(HotKeys.Defaults.Fullscreen!);
        setup.Controller.Hotkeys.Apply(setup.Controller.Preferences.Current.HotKeys);
        var window = setup.Controller.ShowSettings();
        Assert.Equal("Ctrl+Shift+1  taken", window.Recorders[HotKeyAction.Fullscreen].Text);
    }

    [AvaloniaFact]
    public void ChangingASettingWritesIt()
    {
        var (setup, window) = Open();
        window.AfterCapture.SelectedIndex = 1;
        window.Export.SelectedIndex = 1;
        window.Delay.Value = 5;
        window.KeepLibrary.IsChecked = false;
        window.ShowTrayIcon.IsChecked = false;
        var written = Preferences.Load(setup.Controller.Preferences.Path);
        Assert.Equal(AfterCapture.Thumbnail, written.AfterCapture);
        Assert.Equal(ExportScale.OneX, written.ExportScale);
        Assert.Equal(5, written.DelaySeconds);
        Assert.False(written.KeepLibrary);
        Assert.False(written.ShowTrayIcon);
    }

    [AvaloniaFact]
    public void OpenAtLoginFollowsTheSystem()
    {
        var (setup, window) = Open();
        Assert.False(window.OpenAtLogin.IsChecked);
        window.OpenAtLogin.IsChecked = true;
        Assert.True(setup.Platform.Startup.IsEnabled);
    }

    [AvaloniaFact]
    public async Task ClearLibraryAsksAndSparesOpenCaptures()
    {
        var (setup, window) = Open();
        setup.Controller.CaptureFullscreen();
        var open = Assert.Single(setup.Controller.Editors).Entry!;
        setup.Library.Add(CanvasHost.Blank(40, 30), DateTimeOffset.Now.AddHours(-1));
        setup.Dialogs.Confirmation = false;
        await window.ClearLibrary();
        Assert.Equal(2, setup.Library.Entries().Count);
        setup.Dialogs.Confirmation = true;
        await window.ClearLibrary();
        Assert.Equal([open], setup.Library.Entries());
    }

    [AvaloniaFact]
    public void TheSaveFolderShowsItsPath()
    {
        var (_, window) = Open(Preferences.Defaults with { SaveFolder = "~/Pictures/Shots" });
        Assert.Contains("Shots", window.SaveFolder.Text);
    }
}
