using Avalonia;
using Avalonia.Headless;
using Tinysnap.App;
using Tinysnap.Dev;
using Tinysnap.Platform;
using Point = Tinysnap.Core.Point;

[assembly: AvaloniaTestApplication(typeof(Tinysnap.App.Tests.TestApp))]

namespace Tinysnap.App.Tests;

/// <summary>The app as the tests run it: headless, drawn by Skia so rendered frames can be read
/// back, on a platform with no screens unless a test gives it some.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure(() => new TinysnapApp(new FakePlatform(new FakeScreenCapture(() => new FrozenDesktop([], []), () => Point.Zero))))
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
