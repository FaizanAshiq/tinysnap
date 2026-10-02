using SkiaSharp;
using Tinysnap.Core;
using Tinysnap.Platform;

namespace Tinysnap.Linux;

/// <summary>QR codes through <see cref="QrCodes"/>. Text waits for the bundled recogniser.</summary>
// ponytail: no text yet; Tesseract with its languages bundled in the AppImage reads it.
internal sealed class LinuxTextReader : ITextReader
{
    public Task<TextReading?> Read(SKImage image, bool codes) =>
        Task.FromResult(codes ? QrCodes.Read(image) : null);
}
