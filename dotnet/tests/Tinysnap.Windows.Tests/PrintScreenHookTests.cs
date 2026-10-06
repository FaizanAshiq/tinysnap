using System.Diagnostics;
using System.Runtime.InteropServices;
using Tinysnap.Core;

namespace Tinysnap.Windows.Tests;

/// <summary>Windows 11 holds Print Screen for its screen snip and lets go of it only when Explorer
/// restarts, so registering the key fails on a desktop that has it. Tinysnap catches Print Screen
/// before the system sees it. Run on a real Windows desktop, in CI.</summary>
public class PrintScreenHookTests
{
    private const uint PrintScreen = 0x2C;
    private const byte Shift = 0x10;
    private const int Holder = 0xB0B0;

    [Fact]
    public void PrintScreenIsCaughtWhileAnotherAppHoldsIt()
    {
        // This thread holds Print Screen, as the screen snip does on a Windows 11 desktop.
        Assert.True(RegisterHotKey(0, Holder, 0, PrintScreen));
        try
        {
            using var hotkeys = new Win32Hotkeys();
            HotKeyAction? pressed = null;
            hotkeys.Pressed += action => pressed = action;
            Assert.True(hotkeys.Register(HotKeyAction.Area, new HotKeyBinding(PrintScreen, [])));
            Tap((byte)PrintScreen);
            var holderHeard = Pump(() => pressed is not null);
            Assert.Equal(HotKeyAction.Area, pressed);
            Assert.False(holderHeard);
        }
        finally { UnregisterHotKey(0, Holder); }
    }

    /// <summary>With a modifier held, Print Screen is Windows' own, as Alt+Print Screen copies a window.</summary>
    [Fact]
    public void PrintScreenWithAModifierIsLeftToWindows()
    {
        using var hotkeys = new Win32Hotkeys();
        HotKeyAction? pressed = null;
        hotkeys.Pressed += action => pressed = action;
        Assert.True(hotkeys.Register(HotKeyAction.Area, new HotKeyBinding(PrintScreen, [])));
        Tap(Shift, (byte)PrintScreen);
        Pump(() => false, TimeSpan.FromMilliseconds(600));
        Assert.Null(pressed);
    }

    /// <summary>Let go, the key is the system's again at once.</summary>
    [Fact]
    public void PrintScreenIsLetGoWithTheHotkeys()
    {
        var hotkeys = new Win32Hotkeys();
        HotKeyAction? pressed = null;
        hotkeys.Pressed += action => pressed = action;
        Assert.True(hotkeys.Register(HotKeyAction.Area, new HotKeyBinding(PrintScreen, [])));
        hotkeys.Dispose();
        Assert.True(RegisterHotKey(0, Holder, 0, PrintScreen));
        try
        {
            Tap((byte)PrintScreen);
            Assert.True(Pump(() => false, TimeSpan.FromSeconds(1)));
            Assert.Null(pressed);
        }
        finally { UnregisterHotKey(0, Holder); }
    }

    /// <summary>Presses the keys in order and lets them go in reverse, as a hand would.</summary>
    private static void Tap(params byte[] keys)
    {
        foreach (var key in keys) keybd_event(key, 0, 0, 0);
        for (var i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, KeyUp, 0);
    }

    /// <summary>Delivers this thread's messages, which is how the hook is called, until
    /// <paramref name="done"/> or the time is up. True when the held hotkey was heard.</summary>
    private static bool Pump(Func<bool> done, TimeSpan? limit = null)
    {
        var heard = false;
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < (limit ?? TimeSpan.FromSeconds(2)))
        {
            while (PeekMessageW(out var message, 0, 0, 0, Remove))
            {
                if (message.Message == HotkeyMessage && message.Window == 0 && message.WParam == Holder) heard = true;
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
            Thread.Sleep(10);
        }
        return heard;
    }

    private const uint KeyUp = 0x0002, Remove = 0x0001, HotkeyMessage = 0x0312;

    [StructLayout(LayoutKind.Sequential)]
    private struct QueuedMessage
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out QueuedMessage message, nint window, uint first, uint last, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref QueuedMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref QueuedMessage message);
}
