using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Tinysnap.App.Capturing;
using Tinysnap.Core;
using Tinysnap.Platform;
using Style = Avalonia.Styling.Style;

namespace Tinysnap.App;

/// <summary>The whole app, on whichever platform layer it is given: Windows' own, or the
/// stand-in the Mac runs during development.</summary>
/// <param name="started">Called once the app is up, for the development launcher.</param>
/// <param name="files">Pictures the app was opened with, from "Open with", each opened in an editor.</param>
public sealed class TinysnapApp(IPlatform platform, Action<TinysnapApp>? started = null, IReadOnlyList<string>? files = null)
    : Application
{
    public IPlatform Platform { get; } = platform;

    public CaptureController? Captures { get; private set; }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "The colour picker's styles are compiled XAML the trimmed build keeps; Tinysnap.exe self-check loads them.")]
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        // The colour picker ships its own theme, kept apart from Avalonia's main packages.
        Styles.Add(new StyleInclude(new Uri("avares://Tinysnap.App/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });
        // Tooltips after 0.3 s rather than the system's second, which felt slow on a toolbar
        // of seventeen tools.
        Styles.Add(new Style(selector => selector.Is<Control>())
        {
            Setters = { new Setter(ToolTip.ShowDelayProperty, 300) },
        });
        // Every window, the editor and pins included, carries the app's own icon in the taskbar
        // and the window switcher.
        using var icon = AssetLoader.Open(new Uri("avares://Tinysnap.App/Assets/Tinysnap.png"));
        Styles.Add(new Style(selector => selector.Is<Window>())
        {
            Setters = { new Setter(Window.IconProperty, new WindowIcon(icon)) },
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A tray app: closing the last editor leaves it running for the next capture.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var preferences = new PreferencesStore(Preferences.DefaultFilePath);
            Captures = new CaptureController(Platform, preferences, new LibraryStore());
            Captures.StartSweeping();
            if (files is { Count: > 0 }) Captures.OpenFiles(files);
            Tray.Install(this, Platform, Captures, desktop);
            desktop.ShutdownRequested += (_, _) => Platform.Hotkeys.Dispose();
            // Once the app is idle: the first capture then opens as quickly as the rest.
            Dispatcher.UIThread.Post(Captures.WarmUp, DispatcherPriority.Background);
            started?.Invoke(this);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
