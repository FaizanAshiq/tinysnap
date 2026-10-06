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
            using var rest = new Observer();
            using var hotkeys = new Win32Hotkeys();
            HotKeyAction? pressed = null;
            hotkeys.Pressed += action => pressed = action;
            Assert.True(hotkeys.Register(HotKeyAction.Area, new HotKeyBinding(PrintScreen, [])));
            Tap((byte)PrintScreen);
            Pump(() => pressed is not null);
            Assert.Equal(HotKeyAction.Area, pressed);
            // The release too, which comes after the press is heard.
            Pump(() => false, TimeSpan.FromMilliseconds(300));
            Assert.Empty(rest.Saw(PrintScreen));
        }
        finally { UnregisterHotKey(0, Holder); }
    }

    /// <summary>With a modifier held, Print Screen is Windows' own, as Alt+Print Screen copies a window.</summary>
    [Fact]
    public void PrintScreenWithAModifierIsLeftToWindows()
    {
        using var rest = new Observer();
        using var hotkeys = new Win32Hotkeys();
        HotKeyAction? pressed = null;
        hotkeys.Pressed += action => pressed = action;
        Assert.True(hotkeys.Register(HotKeyAction.Area, new HotKeyBinding(PrintScreen, [])));
        Tap(Shift, (byte)PrintScreen);
        Pump(() => rest.Saw(PrintScreen).Count == 2);
        Assert.Null(pressed);
        Assert.Equal(2, rest.Saw(PrintScreen).Count);
    }

    /// <summary>Let go, the key reaches the rest of the system again at once.</summary>
    [Fact]
    public void PrintScreenIsLetGoWithTheHotkeys()
    {
        using var rest = new Observer();
        var hotkeys = new Win32Hotkeys();
        HotKeyAction? pressed = null;
        hotkeys.Pressed += action => pressed = action;
        Assert.True(hotkeys.Register(HotKeyAction.Area, new HotKeyBinding(PrintScreen, [])));
        hotkeys.Dispose();
        Tap((byte)PrintScreen);
        Pump(() => rest.Saw(PrintScreen).Count == 2);
        Assert.Equal(2, rest.Saw(PrintScreen).Count);
        Assert.Null(pressed);
    }

    /// <summary>Presses the keys in order and lets them go in reverse, as a hand would.</summary>
    private static void Tap(params byte[] keys)
    {
        foreach (var key in keys) keybd_event(key, 0, 0, 0);
        for (var i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, KeyUp, 0);
    }

    /// <summary>Delivers this thread's messages, which is how the hooks are called, until
    /// <paramref name="done"/> or the time is up.</summary>
    private static void Pump(Func<bool> done, TimeSpan? limit = null)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < (limit ?? TimeSpan.FromSeconds(2)))
        {
            while (PeekMessageW(out var message, 0, 0, 0, Remove))
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
            Thread.Sleep(10);
        }
    }

    /// <summary>Stands in for everything after Tinysnap: a keyboard hook laid before Tinysnap's, so
    /// it hears a key only when Tinysnap passes it on. A hotkey another app holds would do, but a
    /// CI desktop never delivers one for Print Screen, whatever holds it.</summary>
    private sealed class Observer : IDisposable
    {
        private readonly HookProc procedure;
        private readonly nint hook;
        private readonly List<(uint Key, nint Message)> heard = [];

        public Observer()
        {
            procedure = Hear;
            hook = SetWindowsHookExW(KeyboardLowLevel, procedure, GetModuleHandleW(null), 0);
            Assert.NotEqual(0, hook);
        }

        /// <summary>The downs and ups of <paramref name="key"/> heard so far.</summary>
        public List<nint> Saw(uint key) => heard.Where(entry => entry.Key == key).Select(entry => entry.Message).ToList();

        private nint Hear(int code, nint wParam, nint lParam)
        {
            if (code >= 0) heard.Add(((uint)Marshal.ReadInt32(lParam), wParam));
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        public void Dispose() => UnhookWindowsHookEx(hook);
    }

    private const int KeyboardLowLevel = 13;
    private const uint KeyUp = 0x0002, Remove = 0x0001;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

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
    private static extern nint SetWindowsHookExW(int kind, HookProc procedure, nint module, uint thread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

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
