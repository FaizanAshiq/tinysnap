using System.ComponentModel;
using System.Diagnostics;

namespace Tinysnap.Linux;

/// <summary>A program run to its end, for the GNOME tools Tinysnap drives.</summary>
internal static class Commands
{
    private static long runs, ticks;

    /// <summary>How many programs have run, and how long they took together, for the start log.</summary>
    public static (long Runs, TimeSpan Time) Spent => (Interlocked.Read(ref runs), TimeSpan.FromTicks(Interlocked.Read(ref ticks)));

    /// <summary>Its exit code and what it printed; -1 and nothing when it is not installed.</summary>
    public static (int Exit, string Output) Run(string program, params string[] args)
    {
        var clock = Stopwatch.StartNew();
        try { return RunOnce(program, args); }
        finally
        {
            Interlocked.Increment(ref runs);
            Interlocked.Add(ref ticks, clock.Elapsed.Ticks);
        }
    }

    private static (int Exit, string Output) RunOnce(string program, string[] args)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            // Into the session's log, where a GNOME tool that refused can be found.
            if (process.ExitCode != 0 && error.Result.Trim() is { Length: > 0 } said)
                Console.Error.WriteLine($"tinysnap: {program} {string.Join(' ', args)}: {said}");
            return (process.ExitCode, output.TrimEnd('\n'));
        }
        catch (Win32Exception)
        {
            return (-1, "");
        }
    }
}
