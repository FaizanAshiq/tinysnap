using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using SkiaSharp;
using Tinysnap.App.Capturing;
using Tinysnap.Core;

namespace Tinysnap.App;

/// <summary>The notification area icon. For now its menu captures and quits; the library,
/// settings and the rest of the Mac's menu arrive with milestone 4.</summary>
internal static class Tray
{
    public static TrayIcon Install(Application app, CaptureController captures, HotKeys hotkeys,
                                   IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var menu = new NativeMenu();
        menu.Add(Item(HotKeyAction.Area.Title(), hotkeys.Area, captures.CaptureArea));
        menu.Add(Item(HotKeyAction.Fullscreen.Title(), hotkeys.Fullscreen, captures.CaptureFullscreen));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit Tinysnap", null, () => _ = Quit(captures, lifetime)));
        var tray = new TrayIcon { Icon = Icon(), ToolTipText = "Tinysnap", Menu = menu, IsVisible = true };
        TrayIcon.SetIcons(app, [tray]);
        return tray;
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
