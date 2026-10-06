using Avalonia.Threading;
using Tinysnap.Platform;

namespace Tinysnap.App;

/// <summary>Keeps Tinysnap up to date with no one asking: ten seconds after launch and every six hours
/// it looks for a newer release and downloads it quietly, then installs it the first minute nothing of
/// Tinysnap's is open, so the restart, a moment long, is never in the way. Quit before that, and the
/// download is installed when Tinysnap next starts.</summary>
internal sealed class Updater
{
    private static readonly TimeSpan First = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Between = TimeSpan.FromHours(6);
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);

    private readonly IUpdates updates;
    private readonly Func<bool> idle;
    private readonly Action quit;
    private readonly ITimer timer;
    private bool downloaded;

    /// <param name="idle">True when a restart would close nothing and lose nothing.</param>
    /// <param name="quit">Ends Tinysnap, as Quit does, for the update to install.</param>
    public Updater(IUpdates updates, Func<bool> idle, Action quit, TimeProvider time, Dispatcher ui)
    {
        this.updates = updates;
        this.idle = idle;
        this.quit = quit;
        timer = time.CreateTimer(_ => ui.Post(() => _ = Tick()), null, First, Timeout.InfiniteTimeSpan);
    }

    private async Task Tick()
    {
        if (!downloaded && !(downloaded = await updates.Download() is not null))
        {
            timer.Change(Between, Timeout.InfiniteTimeSpan);
            return;
        }
        if (!idle())
        {
            timer.Change(Retry, Timeout.InfiniteTimeSpan);
            return;
        }
        updates.InstallOnQuit();
        quit();
    }
}
