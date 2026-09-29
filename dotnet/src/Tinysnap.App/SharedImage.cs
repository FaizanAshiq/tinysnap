using SkiaSharp;

namespace Tinysnap.App;

/// <summary>An image held by the control that made it and by every draw operation still
/// showing it, disposed when the last lets go. A render replaced on the UI thread can still be
/// mid-draw on the render thread, and disposing it there crashed natively; left to the
/// finalizer, a 5K capture's renders piled up out of the collector's sight.</summary>
internal sealed class SharedImage(SKImage image)
{
    private int holders = 1;

    public SKImage Image { get; } = image;

    public SharedImage Acquire()
    {
        Interlocked.Increment(ref holders);
        return this;
    }

    public void Release()
    {
        if (Interlocked.Decrement(ref holders) == 0) Image.Dispose();
    }
}
