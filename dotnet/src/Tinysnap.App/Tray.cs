using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using SkiaSharp;
using Tinysnap.App.Capturing;
using Tinysnap.Core;

namespace Tinysnap.App;

/// <summary>The notification area icon and its menu: every action with its hotkey, Settings and
/// Quit. The menu is built again whenever what it shows changes, so the hotkeys, the countdown
/// and what can be repeated are always current.</summary>
internal static class Tray
{
    public static TrayIcon Install(Application app, CaptureController captures, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var tray = new TrayIcon { Icon = Icon() };
        void Rebuild()
        {
            tray.Menu = Menu(captures, () => _ = Quit(captures, lifetime));
            tray.IsVisible = captures.Preferences.Current.ShowTrayIcon;
            tray.ToolTipText = captures.SecondsLeft is { } left ? $"Tinysnap, capturing in {left}" : "Tinysnap";
        }
        Rebuild();
        captures.StateChanged += Rebuild;
        captures.Hotkeys.Changed += Rebuild;
        captures.Preferences.Changed += _ => Rebuild();
        // The menu opens on a right click; a left click captures an area, the thing done most.
        tray.Clicked += (_, _) => Click(captures);
        TrayIcon.SetIcons(app, [tray]);
        return tray;
    }

    /// <summary>What a left click on the icon does.</summary>
    internal static void Click(CaptureController captures) => captures.Perform(HotKeyAction.Area);

    /// <summary>Every action this build has, with its hotkey and "(taken)" when another app holds
    /// it. Repeat Last Area waits for a first area, and while a delayed capture counts down its
    /// item shows the seconds left and every capture waits.</summary>
    public static NativeMenu Menu(CaptureController captures, Action quit)
    {
        var menu = new NativeMenu();
        var hotkeys = captures.Preferences.Current.HotKeys;
        foreach (var action in HotkeyRegistrar.Available)
        {
            var title = action == HotKeyAction.Delayed && captures.SecondsLeft is { } left ? $"Capturing in {left}" : action.Title();
            if (captures.Hotkeys.Taken.Contains(action)) title += " (taken)";
            var item = Item(title, hotkeys[action], () => captures.Perform(action));
            item.IsEnabled = captures.SecondsLeft is null && (action != HotKeyAction.RepeatArea || captures.HasLastArea);
            menu.Add(item);
        }
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Settings...", null, () => captures.ShowSettings()));
        menu.Add(Item("Quit Tinysnap", null, quit));
        return menu;
    }

    /// <summary>Quits once every editor has closed, each with edits asking first; Cancel on any
    /// keeps the app running.</summary>
    private static async Task Quit(CaptureController captures, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        if (await captures.CloseAll()) lifetime.Shutdown();
    }

    private static NativeMenuItem Item(string title, HotKeyBinding? binding, Action action)
    {
        var item = new NativeMenuItem(title);
        if (binding is not null && Gesture(binding) is { } gesture) item.Gesture = gesture;
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>The hotkey shown beside its item, for the letters and digits Avalonia can name.</summary>
    private static KeyGesture? Gesture(HotKeyBinding binding)
    {
        Key? key = binding.KeyCode switch
        {
            >= 0x30 and <= 0x39 => Key.D0 + (int)(binding.KeyCode - 0x30),
            >= 0x41 and <= 0x5A => Key.A + (int)(binding.KeyCode - 0x41),
            _ => null,
        };
        if (key is not { } named) return null;
        var modifiers = KeyModifiers.None;
        foreach (var modifier in binding.Modifiers)
        {
            modifiers |= modifier switch
            {
                ModifierKey.Control => KeyModifiers.Control,
                ModifierKey.Shift => KeyModifiers.Shift,
                ModifierKey.Alt => KeyModifiers.Alt,
                _ => KeyModifiers.Meta,
            };
        }
        return new KeyGesture(named, modifiers);
    }

    /// <summary>A viewfinder: four corner marks round a dot, drawn once at 32 pixels.</summary>
    private static WindowIcon Icon()
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 32));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var ink = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            StrokeCap = SKStrokeCap.Round,
        };
        foreach (var (x, y, dx, dy) in new[] { (4f, 4f, 1f, 1f), (28f, 4f, -1f, 1f), (4f, 28f, 1f, -1f), (28f, 28f, -1f, -1f) })
        {
            canvas.DrawLine(x, y, x + 8 * dx, y, ink);
            canvas.DrawLine(x, y, x, y + 8 * dy, ink);
        }
        ink.Style = SKPaintStyle.Fill;
        canvas.DrawCircle(16, 16, 4, ink);
        using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        return new WindowIcon(new MemoryStream(png.ToArray()));
    }
}
