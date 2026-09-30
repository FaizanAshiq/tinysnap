using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Tinysnap.Core;

namespace Tinysnap.App.Settings;

/// <summary>Click it, press a combination, done. Delete or Backspace clears it, Esc backs out.
/// It records one binding and hands it back, knowing nothing of where it is stored.</summary>
internal sealed class HotKeyRecorder : Button
{
    private HotKeyBinding? binding;
    private bool isTaken;

    /// <summary>Asked before a binding is kept; false refuses it, for example one another action
    /// already uses.</summary>
    public Func<HotKeyBinding?, bool>? Record { get; init; }

    /// <summary>Recording started or stopped; the app lets its hotkeys go meanwhile.</summary>
    public event Action<bool>? RecordingChanged;

    public bool IsRecording { get; private set; }

    /// <summary>Drawn as a button: a control theme is looked up by exact type.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    /// <param name="name">What a screen reader calls the field, for example "Capture Area hotkey".</param>
    public HotKeyRecorder(HotKeyBinding? binding, string name)
    {
        this.binding = binding;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(this, name);
        Click += (_, _) => StartRecording();
        Refresh();
    }

    public HotKeyBinding? Binding
    {
        get => binding;
        set
        {
            binding = value;
            Refresh();
        }
    }

    /// <summary>Another app holds the combination, so it does nothing right now.</summary>
    public bool IsTaken
    {
        get => isTaken;
        set
        {
            isTaken = value;
            Refresh();
        }
    }

    internal string Text => (string)Content!;

    public void StartRecording()
    {
        if (IsRecording) return;
        IsRecording = true;
        Focus();
        Refresh();
        RecordingChanged?.Invoke(true);
    }

    /// <summary>Stops without binding anything. A field left armed would rebind on the next key
    /// pressed anywhere in the window.</summary>
    public void StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        Refresh();
        RecordingChanged?.Invoke(false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsRecording)
        {
            // Space or Enter arms it from the keyboard, through the button's own click.
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape:
                StopRecording();
                return;
            case Key.Delete or Key.Back:
                Finish(null);
                return;
        }
        var modifiers = Modifiers(e.KeyModifiers);
        // A hotkey with no modifier is held system wide, so it would swallow that key in every
        // other app as well. A modifier on its own waits for the key that goes with it.
        if (modifiers.Count == 0 || VirtualKey(e.Key) is not { } code) return;
        Finish(new HotKeyBinding(code, [.. modifiers]));
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        StopRecording();
    }

    private void Finish(HotKeyBinding? next)
    {
        StopRecording();
        if (Record?.Invoke(next) ?? true) Binding = next;
    }

    private void Refresh() => Content = IsRecording ? "Press a combination"
        : binding is null ? "None"
        : isTaken ? $"{binding.DisplayString}  taken"
        : binding.DisplayString;

    private static List<ModifierKey> Modifiers(KeyModifiers keys)
    {
        var modifiers = new List<ModifierKey>();
        if (keys.HasFlag(KeyModifiers.Control)) modifiers.Add(ModifierKey.Control);
        if (keys.HasFlag(KeyModifiers.Alt)) modifiers.Add(ModifierKey.Alt);
        if (keys.HasFlag(KeyModifiers.Shift)) modifiers.Add(ModifierKey.Shift);
        if (keys.HasFlag(KeyModifiers.Meta)) modifiers.Add(ModifierKey.Windows);
        return modifiers;
    }

    /// <summary>The Windows virtual key code for the keys worth binding a hotkey to.</summary>
    internal static uint? VirtualKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (uint)(0x41 + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (uint)(0x30 + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (uint)(0x60 + (key - Key.NumPad0)),
        >= Key.F1 and <= Key.F24 => (uint)(0x70 + (key - Key.F1)),
        Key.Space => 0x20,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.End => 0x23,
        Key.Home => 0x24,
        Key.Left => 0x25,
        Key.Up => 0x26,
        Key.Right => 0x27,
        Key.Down => 0x28,
        Key.PrintScreen => 0x2C,
        Key.Insert => 0x2D,
        Key.OemSemicolon => 0xBA,
        Key.OemPlus => 0xBB,
        Key.OemComma => 0xBC,
        Key.OemMinus => 0xBD,
        Key.OemPeriod => 0xBE,
        Key.OemQuestion => 0xBF,
        Key.OemTilde => 0xC0,
        Key.OemOpenBrackets => 0xDB,
        Key.OemPipe => 0xDC,
        Key.OemCloseBrackets => 0xDD,
        Key.OemQuotes => 0xDE,
        _ => null,
    };
}
