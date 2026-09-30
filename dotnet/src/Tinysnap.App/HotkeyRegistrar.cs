using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.App;

/// <summary>Holds the global hotkeys the preferences ask for, notes which another app already
/// holds, and lets them all go while a Settings field records, so pressing a combination there
/// does not fire the capture it is being set to.</summary>
internal sealed class HotkeyRegistrar(IHotkeys platform)
{
    /// <summary>The actions this build carries out. Capture Text and Scan QR Code join them with
    /// their milestone, so a key is never held for something that does nothing.</summary>
    public static readonly IReadOnlyList<HotKeyAction> Available =
        [HotKeyAction.Area, HotKeyAction.Fullscreen, HotKeyAction.RepeatArea, HotKeyAction.Delayed, HotKeyAction.Library];

    private HotKeys? wanted;
    private bool paused;

    /// <summary>Actions whose combination another app holds, so it does nothing right now.</summary>
    public IReadOnlySet<HotKeyAction> Taken { get; private set; } = new HashSet<HotKeyAction>();

    public event Action? Changed;

    public void Apply(HotKeys hotkeys)
    {
        wanted = hotkeys;
        if (paused) return;
        platform.UnregisterAll();
        Taken = Available.Where(action => hotkeys[action] is { } binding && !platform.Register(action, binding)).ToHashSet();
        Changed?.Invoke();
    }

    public void Pause()
    {
        paused = true;
        platform.UnregisterAll();
    }

    public void Resume()
    {
        paused = false;
        if (wanted is not null) Apply(wanted);
    }
}
