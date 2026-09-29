using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using Tinysnap.Platform;

namespace Tinysnap.Windows;

/// <summary>The Recycle Bin and Explorer, through the framework's own shell calls.</summary>
internal sealed class Win32Files : IFileActions
{
    public bool MoveToRecycleBin(string path)
    {
        try
        {
            if (Directory.Exists(path))
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else if (File.Exists(path))
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                return false;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    public void Reveal(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
}

/// <summary>Start at login through the per-user Run key, which needs no admin rights.</summary>
internal sealed class Win32Startup(string valueName = "Tinysnap") : IStartup
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled
    {
        get
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return run?.GetValue(valueName) is string;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
                run.SetValue(valueName, $"\"{Environment.ProcessPath}\"");
            else
                run.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
