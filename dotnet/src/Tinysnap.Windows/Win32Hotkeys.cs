using System.Runtime.InteropServices;
using Tinysnap.Core;
using Tinysnap.Platform;
using static Tinysnap.Windows.Native;

namespace Tinysnap.Windows;

/// <summary>System wide hotkeys through RegisterHotKey on one message-only window, whose one
/// handler dispatches on the id: a handler per hotkey is how one hotkey ends up firing another's
/// action. Made on the UI thread, whose message loop delivers WM_HOTKEY.</summary>
internal sealed class Win32Hotkeys : IHotkeys
{
    private const string ClassName = "TinysnapHotkeys";
    private static readonly object Registration = new();
    private static bool isRegistered;

    /// <summary>Held here, or the collector frees the procedure Windows still calls.</summary>
    private readonly WindowProc procedure;
    private readonly nint window;
    private readonly Dictionary<int, HotKeyAction> actions = [];
    private bool isDisposed;
    private PrintScreenHook? printScreen;
    private int printScreenId;

    public event Action<HotKeyAction>? Pressed;

    public Win32Hotkeys()
    {
        procedure = Receive;
        var instance = GetModuleHandleW(null);
        lock (Registration)
        {
            if (!isRegistered)
            {
                var windowClass = new WNDCLASSEX
                {
                    cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate<WindowProc>(Route),
                    hInstance = instance,
                    lpszClassName = ClassName,
                };
                isRegistered = RegisterClassExW(ref windowClass) != 0;
            }
        }
        window = CreateWindowExW(0, ClassName, "", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, instance, 0);
        Windows[window] = this;
    }

    /// <summary>One class procedure for every instance, handing each message to its window's owner.</summary>
    private static readonly Dictionary<nint, Win32Hotkeys> Windows = [];

    private static readonly WindowProc Route = (window, message, wParam, lParam) =>
        Windows.TryGetValue(window, out var owner) ? owner.procedure(window, message, wParam, lParam)
            : DefWindowProcW(window, message, wParam, lParam);

    private nint Receive(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WM_HOTKEY && actions.TryGetValue((int)wParam, out var action))
        {
            Pressed?.Invoke(action);
            return 0;
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    public bool Register(HotKeyAction action, HotKeyBinding binding)
    {
        var id = (int)action + 1;
        UnregisterHotKey(window, id);
        actions.Remove(id);
        // Print Screen moved to another key goes back to the system.
        if (id == printScreenId) LetGoOfPrintScreen();
        var modifiers = MOD_NOREPEAT;
        foreach (var key in binding.Modifiers)
        {
            modifiers |= key switch
            {
                ModifierKey.Control => MOD_CONTROL,
                ModifierKey.Shift => MOD_SHIFT,
                ModifierKey.Alt => MOD_ALT,
                _ => MOD_WIN,
            };
        }
        // Print Screen alone is taken by a keyboard hook and comes back as the relay key, which is
        // what is registered: Windows 11 holds Print Screen itself until Explorer restarts.
        if (binding.KeyCode == 0x2C && binding.Modifiers.IsEmpty && TakePrintScreen(id))
        {
            actions[id] = action;
            return true;
        }
        if (!RegisterHotKey(window, id, modifiers, binding.KeyCode)) return false;
        actions[id] = action;
        return true;
    }

    /// <summary>False when the hook or the relay key is refused, and Print Screen is registered as any
    /// other key instead, which works wherever nothing else holds it.</summary>
    private bool TakePrintScreen(int id)
    {
        if (!RegisterHotKey(window, id, MOD_NOREPEAT, PrintScreenHook.Relay)) return false;
        printScreen ??= new PrintScreenHook();
        if (printScreen.IsActive)
        {
            printScreenId = id;
            return true;
        }
        UnregisterHotKey(window, id);
        LetGoOfPrintScreen();
        return false;
    }

    private void LetGoOfPrintScreen()
    {
        printScreen?.Dispose();
        printScreen = null;
        printScreenId = 0;
    }

    public void UnregisterAll()
    {
        foreach (var id in actions.Keys) UnregisterHotKey(window, id);
        actions.Clear();
        LetGoOfPrintScreen();
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        UnregisterAll();
        Windows.Remove(window);
        DestroyWindow(window);
    }
}
