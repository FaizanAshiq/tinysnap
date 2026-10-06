using Avalonia.Threading;
using Tinysnap.Platform;

namespace Tinysnap.App;

/// <summary>Keeps Tinysnap up to date with no one asking: once a week it looks for a newer release
/// and downloads it quietly, then installs it the first minute nothing of Tinysnap's is open, so the
/// restart, a moment long, is never in the way. Quit before that, and the download is installed when
/// Tinysnap next starts. The time of the last look is kept on disk, so a restart, as at every login,
/// does not bring the next one forward.</summary>
internal sealed class Updater
{
    private static readonly TimeSpan First = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);

    private readonly IUpdates updates;
    private readonly Func<bool> idle;
    private readonly Action quit;
    private readonly TimeProvider time;
    private readonly string stamp;
    private readonly ITimer timer;
    private bool downloaded;

    /// <param name="idle">True when a restart would close nothing and lose nothing.</param>
    /// <param name="quit">Ends Tinysnap, as Quit does, for the update to install.</param>
    /// <param name="stamp">The file holding the time of the last look.</param>
    public Updater(IUpdates updates, Func<bool> idle, Action quit, TimeProvider time, Dispatcher ui, string stamp)
    {
        this.updates = updates;
        this.idle = idle;
        this.quit = quit;
        this.time = time;
        this.stamp = stamp;
        var due = LastLook() + Week - time.GetUtcNow();
        timer = time.CreateTimer(_ => ui.Post(() => _ = Tick()), null, due > First ? due : First, Timeout.InfiniteTimeSpan);
    }

    private async Task Tick()
    {
        if (!downloaded)
        {
            Remember(time.GetUtcNow());
            if (!(downloaded = await updates.Download() is not null))
            {
                timer.Change(Week, Timeout.InfiniteTimeSpan);
                return;
            }
        }
        if (!idle())
        {
            timer.Change(Retry, Timeout.InfiniteTimeSpan);
            return;
        }
        updates.InstallOnQuit();
        quit();
    }

    /// <summary>Never, when the file is missing or unreadable, which makes the next look the first.</summary>
    private DateTimeOffset LastLook()
    {
        try
        {
            return DateTimeOffset.TryParse(File.ReadAllText(stamp), out var last) ? last : DateTimeOffset.MinValue;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    private void Remember(DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stamp)!);
            File.WriteAllText(stamp, now.ToString("O"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Unwritten, the next start looks again rather than waiting a week: harmless.
        }
    }
}
