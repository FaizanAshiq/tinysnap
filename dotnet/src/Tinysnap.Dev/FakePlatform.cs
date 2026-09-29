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

/// <summary>A clipboard that keeps what it was given, for the tests to read back.</summary>
public sealed class FakeClipboard : IClipboard
{
    public SkiaSharp.SKImage? Image { get; private set; }
    public byte[]? Png { get; private set; }
    public double Dpi { get; private set; }
    public string? Text { get; private set; }
    public uint ChangeCount { get; private set; }

    public bool SetImage(SkiaSharp.SKImage image, byte[] png, double dpi)
    {
        (Image, Png, Dpi, Text) = (image, png, dpi, null);
        ChangeCount++;
        return true;
    }

    public bool SetText(string text)
    {
        (Image, Png, Text) = (null, null, text);
        ChangeCount++;
        return true;
    }
}

/// <summary>The fake screens and clipboard with no global hotkeys: the development launcher's
/// buttons stand in for them.</summary>
public sealed class FakePlatform(IScreenCapture screen) : IPlatform
{
    public IScreenCapture Screen { get; } = screen;

    public IHotkeys Hotkeys { get; } = new NoHotkeys();

    // ponytail: the development build copies to this fake, not the Mac's own clipboard; an
    // Avalonia clipboard stand-in if copying on a Mac is ever wanted.
    public IClipboard Clipboard { get; } = new FakeClipboard();

    private sealed class NoHotkeys : IHotkeys
    {
        public event Action<HotKeyAction>? Pressed { add { } remove { } }

        public bool Register(HotKeyAction action, HotKeyBinding binding) => true;

        public void UnregisterAll() { }

        public void Dispose() { }
    }
}
