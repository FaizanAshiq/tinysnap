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

    /// <summary>Only http and https reach here: a reading offers Open Link for nothing else.</summary>
    public void Open(Uri link) => Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true });

    /// <summary>The picture Windows reports for the desktop.</summary>
    // ponytail: one picture for every monitor; IDesktopWallpaper per monitor if they differ.
    public SkiaSharp.SKImage? Wallpaper()
    {
        var path = new char[1024];
        if (!Native.SystemParametersInfoW(Native.SPI_GETDESKWALLPAPER, (uint)path.Length, path, 0)) return null;
        var file = new string(path).TrimEnd('\0');
        return File.Exists(file) ? SkiaSharp.SKImage.FromEncodedData(file) : null;
    }
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
