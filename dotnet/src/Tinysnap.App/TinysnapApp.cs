using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Tinysnap.App.Capturing;
using Tinysnap.Core;
using Tinysnap.Platform;
using Style = Avalonia.Styling.Style;

namespace Tinysnap.App;

/// <summary>The whole app, on whichever platform layer it is given: Windows' own, or the
/// stand-in the Mac runs during development.</summary>
/// <param name="started">Called once the app is up, for the development launcher.</param>
public sealed class TinysnapApp(IPlatform platform, Action<TinysnapApp>? started = null) : Application
{
    public IPlatform Platform { get; } = platform;

    public CaptureController? Captures { get; private set; }

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
            Tray.Install(this, Captures, preferences.Current.HotKeys, desktop);
            ListenForHotkeys(preferences.Current.HotKeys);
            desktop.ShutdownRequested += (_, _) => Platform.Hotkeys.Dispose();
            started?.Invoke(this);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Capture Area and Capture Fullscreen for now; the other actions register as their
    /// milestones land, so a key is never held for something that does nothing.</summary>
    private void ListenForHotkeys(HotKeys hotkeys)
    {
        Platform.Hotkeys.Pressed += action => Dispatcher.UIThread.Post(() =>
        {
            if (action == HotKeyAction.Area) Captures?.CaptureArea();
            else if (action == HotKeyAction.Fullscreen) Captures?.CaptureFullscreen();
        });
    }
}
