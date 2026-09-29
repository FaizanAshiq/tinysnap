using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.Dev;

/// <summary>A screen capture that hands back whatever desktop it is given, for the tests and for
/// running the app on a Mac, which has no Windows screens to read.</summary>
public sealed class FakeScreenCapture(Func<FrozenDesktop> freeze, Func<Point> pointer, double windowCornerRadius = 0)
    : IScreenCapture
{
    public FrozenDesktop Freeze() => freeze();

    public Point PointerPosition() => pointer();

    public double WindowCornerRadius => windowCornerRadius;
}

/// <summary>The fake screens with no global hotkeys: the development launcher's buttons stand in
/// for them.</summary>
public sealed class FakePlatform(IScreenCapture screen) : IPlatform
{
    public IScreenCapture Screen { get; } = screen;

    public IHotkeys Hotkeys { get; } = new NoHotkeys();

    private sealed class NoHotkeys : IHotkeys
    {
        public event Action<HotKeyAction>? Pressed { add { } remove { } }

        public bool Register(HotKeyAction action, HotKeyBinding binding) => true;

        public void UnregisterAll() { }

        public void Dispose() { }
    }
}
