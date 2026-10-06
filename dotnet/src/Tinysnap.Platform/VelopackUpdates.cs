using Velopack;
using Velopack.Sources;

namespace Tinysnap.Platform;

/// <summary>Updates from the GitHub releases, through Velopack, which installed Tinysnap: each release
/// carries a feed per platform and processor, and this copy reads its own. The download goes to
/// Velopack's own folder and is installed by its updater once Tinysnap quits, or at the next start.</summary>
public sealed class VelopackUpdates : IUpdates
{
    /// <summary>Where updates come from instead of the releases, a folder or an address, so the
    /// end-to-end test can update to a build of its own.</summary>
    public const string FeedVariable = "TINYSNAP_UPDATE_FEED";

    private readonly UpdateManager manager;
    private UpdateInfo? downloaded;

    public VelopackUpdates(string? feed = null) =>
        manager = feed is { Length: > 0 } ? new UpdateManager(feed)
            : new UpdateManager(new GithubSource("https://github.com/FaizanAshiq/tinysnap", null, false));

    public async Task<string?> Download()
    {
        // Run from a build folder rather than installed, there is nothing to update.
        if (!manager.IsInstalled) return null;
        try
        {
            if (await manager.CheckForUpdatesAsync() is not { } update) return null;
            await manager.DownloadUpdatesAsync(update);
            downloaded = update;
            return update.TargetFullRelease.Version.ToString();
        }
        catch (Exception error)
        {
            // Offline, GitHub refusing or down, a full disk: the next round tries again.
            Console.Error.WriteLine($"tinysnap: no update this time: {error.Message}");
            return null;
        }
    }

    public void InstallOnQuit()
    {
        if (downloaded is not null) manager.WaitExitThenApplyUpdates(downloaded.TargetFullRelease, silent: true, restart: true);
    }
}
