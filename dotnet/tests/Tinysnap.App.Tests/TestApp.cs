using Avalonia;
using Avalonia.Headless;
using Tinysnap.App;
using Tinysnap.Core;
using Tinysnap.Platform;
using Point = Tinysnap.Core.Point;

[assembly: AvaloniaTestApplication(typeof(Tinysnap.App.Tests.TestApp))]

namespace Tinysnap.App.Tests;

/// <summary>The app as the tests run it: headless, drawn by Skia so rendered frames can be read
/// back, on a platform with no screens or hotkeys unless a test gives it some.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure(() => new TinysnapApp(new TestPlatform()))
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal sealed class TestPlatform : IPlatform
{
    public IScreenCapture Screen { get; init; } = new NoScreens();
    public IHotkeys Hotkeys { get; } = new NoHotkeys();

    private sealed class NoScreens : IScreenCapture
    {
        public FrozenDesktop Freeze() => new([], []);
        public Point PointerPosition() => Point.Zero;
        public double WindowCornerRadius => 0;
    }

    private sealed class NoHotkeys : IHotkeys
    {
        public event Action<HotKeyAction>? Pressed { add { } remove { } }
        public bool Register(HotKeyAction action, HotKeyBinding binding) => true;
        public void UnregisterAll() { }
        public void Dispose() { }
    }
}
