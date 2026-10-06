using System.Runtime.InteropServices;
using static Tinysnap.Windows.Native;

namespace Tinysnap.Windows;

/// <summary>Print Screen alone, taken before Windows hands it to whatever holds it: the screen snip
/// in Explorer, the Snipping Tool, or another app. Windows 11 lets go of the key only when Explorer
/// restarts, whatever its setting says, so registering the key fails on a desktop that has it.
///
/// Each press is passed on as <see cref="Relay"/>, a key no keyboard has, which Tinysnap registers
/// in its place: the press then arrives as a real hotkey, which lets the overlay take the keyboard.
/// The hook runs on a thread of its own, since Windows drops a hook that answers late, and it is
/// laid again now and then, ahead of any hook another app adds after it.</summary>
internal sealed class PrintScreenHook : IDisposable
{
    /// <summary>F24, on no keyboard, so nothing else holds it.</summary>
    public const uint Relay = 0x87;

    private const int PrintScreen = 0x2C;
    private const uint RenewEvery = 30_000;

    /// <summary>Shift, Ctrl, Alt and both Windows keys: with one held, Print Screen is Windows' own,
    /// as Alt+Print Screen copies the window in front.</summary>
    private static readonly int[] Modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C];

    /// <summary>Held here, or the collector frees the procedure Windows still calls.</summary>
    private readonly HookProc procedure;
    private readonly Thread thread;
    private readonly ManualResetEventSlim started = new();
    private uint threadId;
    private nint hook;
    private bool taking;

    public PrintScreenHook()
    {
        procedure = Key;
        thread = new Thread(Run) { IsBackground = true, Name = "Print Screen" };
        thread.Start();
        started.Wait();
    }

    /// <summary>False when Windows refused the hook, and Print Screen is left as it was.</summary>
    public bool IsActive => hook != 0;

    private void Run()
    {
        threadId = GetCurrentThreadId();
        hook = SetWindowsHookExW(WH_KEYBOARD_LL, procedure, GetModuleHandleW(null), 0);
        SetTimer(0, 0, RenewEvery, 0);
        started.Set();
        // Windows calls the hook on this thread while it waits here.
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            if (message.message == WM_TIMER) Renew();
        }
        if (hook != 0) UnhookWindowsHookEx(hook);
    }

    /// <summary>A fresh hook goes ahead of every other, then the old one is let go: no gap between.</summary>
    private void Renew()
    {
        var fresh = SetWindowsHookExW(WH_KEYBOARD_LL, procedure, GetModuleHandleW(null), 0);
        if (fresh == 0) return;
        UnhookWindowsHookEx(hook);
        hook = fresh;
    }

    private nint Key(int code, nint wParam, nint lParam)
    {
        if (code < 0 || Marshal.ReadInt32(lParam) != PrintScreen) return CallNextHookEx(hook, code, wParam, lParam);
        var down = (uint)wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
        // The repeats of a press already taken, and its release, are kept from the rest of the system.
        if (taking)
        {
            if (!down) taking = false;
            return 1;
        }
        if (!down || ModifierHeld()) return CallNextHookEx(hook, code, wParam, lParam);
        taking = true;
        keybd_event((byte)Relay, 0, 0, 0);
        keybd_event((byte)Relay, 0, KEYEVENTF_KEYUP, 0);
        return 1;
    }

    private static bool ModifierHeld()
    {
        foreach (var key in Modifiers)
            if (GetAsyncKeyState(key) < 0) return true;
        return false;
    }

    public void Dispose()
    {
        PostThreadMessageW(threadId, WM_QUIT, 0, 0);
        thread.Join(TimeSpan.FromSeconds(1));
        started.Dispose();
    }
}
