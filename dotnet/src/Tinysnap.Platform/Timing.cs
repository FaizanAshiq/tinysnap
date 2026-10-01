using System.Diagnostics;

namespace Tinysnap.Platform;

// TEMPORARY: measuring where the time goes between a hotkey and the overlay. Removed before merge.
public static class Timing
{
    private static readonly string? File = Environment.GetEnvironmentVariable("TINYSNAP_TRACE");
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();

    public static void Mark(string what)
    {
        if (File is null) return;
        try { lock (Gate) System.IO.File.AppendAllText(File, $"{Clock.Elapsed.TotalMilliseconds,10:F1}  {what}{Environment.NewLine}"); }
        catch (IOException) { }
    }
}
