using System.ComponentModel;
using System.Diagnostics;

namespace Tinysnap.Linux;

/// <summary>A program run to its end, for the GNOME tools Tinysnap drives.</summary>
internal static class Commands
{
    /// <summary>Its exit code and what it printed; -1 and nothing when it is not installed.</summary>
    public static (int Exit, string Output) Run(string program, params string[] args)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output.TrimEnd('\n'));
        }
        catch (Win32Exception)
        {
            return (-1, "");
        }
    }
}
