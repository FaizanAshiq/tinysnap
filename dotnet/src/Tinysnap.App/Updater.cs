using Avalonia.Threading;
using Tinysnap.Platform;

namespace Tinysnap.App;

/// <summary>Keeps Tinysnap up to date with no one asking: once a week it looks for a newer release
/// and downloads it quietly, then installs it the first minute nothing of Tinysnap's is open, so the
/// restart, a moment long, is never in the way. Quit before that, and the download is installed when
/// Tinysnap next starts. The time of the last look is kept on disk, so a restart, as at every login,
/// does not bring the next one forward. The week is counted by the calendar: a timer stops while the
/// computer sleeps, so one set a week ahead would run late by every night of sleep, and the date is
/// checked at least hourly instead.</summary>
internal sealed class Updater
{
    private static readonly TimeSpan First = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);

    private readonly IUpdates updates;
    private readonly Func<bool> idle;
    private readonly Action quit;
    private readonly TimeProvider time;
    private readonly string stamp;
    private readonly ITimer timer;
    private DateTimeOffset last;
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
        last = LastLook();
        var wait = Wait();
        timer = time.CreateTimer(_ => ui.Post(() => _ = Tick()), null, wait > First ? wait : First, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Until the next check whether a week has passed since the last look, at most an hour;
    /// zero when it has.</summary>
    private TimeSpan Wait()
    {
        var left = last + Week - time.GetUtcNow();
        return left <= TimeSpan.Zero ? TimeSpan.Zero : left < Hour ? left : Hour;
    }

    private async Task Tick()
    {
        if (!downloaded)
        {
            var wait = Wait();
            if (wait > TimeSpan.Zero)
            {
                timer.Change(wait, Timeout.InfiniteTimeSpan);
                return;
            }
            last = time.GetUtcNow();
            Remember(last);
            if (!(downloaded = await updates.Download() is not null))
            {
                timer.Change(Hour, Timeout.InfiniteTimeSpan);
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
