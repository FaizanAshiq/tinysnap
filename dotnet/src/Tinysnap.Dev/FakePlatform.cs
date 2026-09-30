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
        // A copy, as a real clipboard keeps: the caller disposes its image once this returns.
        (Image, Png, Dpi, Text) = (SkiaSharp.SKImage.FromEncodedData(png), png, dpi, null);
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

/// <summary>A recycle bin of its own in the temporary folder, so neither the tests nor the
/// development build ever fill the real one, and a record of what was revealed.</summary>
public sealed class FakeFiles : IFileActions
{
    public static string Bin => Path.Combine(Path.GetTempPath(), "tinysnap-recycle-bin");

    public List<string> Recycled { get; } = [];

    public List<string> Revealed { get; } = [];

    public bool MoveToRecycleBin(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return false;
        Directory.CreateDirectory(Bin);
        var target = Path.Combine(Bin, $"{Path.GetFileName(path)} {Guid.NewGuid():N}");
        if (Directory.Exists(path)) Directory.Move(path, target);
        else File.Move(path, target);
        Recycled.Add(path);
        return true;
    }

    public void Reveal(string path) => Revealed.Add(path);
}

public sealed class FakeStartup : IStartup
{
    public bool IsEnabled { get; private set; }

    public bool SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        return true;
    }
}

/// <summary>The fake screens and clipboard with no global hotkeys: the development launcher's
/// buttons stand in for them.</summary>
public sealed class FakePlatform(IScreenCapture screen) : IPlatform
{
    public IScreenCapture Screen { get; } = screen;

    public IHotkeys Hotkeys { get; } = new FakeHotkeys();

    // ponytail: the development build copies to this fake, not the Mac's own clipboard; an
    // Avalonia clipboard stand-in if copying on a Mac is ever wanted.
    public IClipboard Clipboard { get; } = new FakeClipboard();

    public bool ReduceMotion => false;

    public IFileActions Files { get; } = new FakeFiles();

    public IStartup Startup { get; } = new FakeStartup();
}

/// <summary>Global hotkeys that go nowhere: what is registered is recorded, a binding in
/// <see cref="HeldElsewhere"/> is refused as another app would refuse it, and
/// <see cref="Press"/> stands in for the key.</summary>
public sealed class FakeHotkeys : IHotkeys
{
    public Dictionary<HotKeyAction, HotKeyBinding> Registered { get; } = [];

    public HashSet<HotKeyBinding> HeldElsewhere { get; } = [];

    public event Action<HotKeyAction>? Pressed;

    public bool Register(HotKeyAction action, HotKeyBinding binding)
    {
        if (HeldElsewhere.Contains(binding)) return false;
        Registered[action] = binding;
        return true;
    }

    public void UnregisterAll() => Registered.Clear();

    public void Press(HotKeyAction action) => Pressed?.Invoke(action);

    public void Dispose() => UnregisterAll();
}
