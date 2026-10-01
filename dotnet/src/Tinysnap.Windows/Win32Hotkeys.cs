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
            Tinysnap.Platform.Timing.Mark("hotkey message");
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
        if (!RegisterHotKey(window, id, modifiers, binding.KeyCode)) return false;
        actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in actions.Keys) UnregisterHotKey(window, id);
        actions.Clear();
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
