namespace Tinysnap.Linux.Tests;

public class DesktopEntriesTests
{
    private readonly string data = Directory.CreateTempSubdirectory().FullName;
    private readonly string config = Directory.CreateTempSubdirectory().FullName;
    private DesktopEntries Entries() => new(data, config, (_, _) => (0, ""));

    private string Menu => Path.Combine(data, "applications", "com.faizanashiq.Tinysnap.desktop");
    private string Autostart => Path.Combine(config, "autostart", "com.faizanashiq.Tinysnap.desktop");
    private string Icon => Path.Combine(data, "icons", "hicolor", "256x256", "apps", "com.faizanashiq.Tinysnap.png");

    [Fact]
    public void TheMenuEntryOpensPicturesAndFollowsTheAppImage()
    {
        var entries = Entries();
        entries.Write("/home/sam/Applications/Tinysnap.AppImage", [1, 2, 3]);
        var text = File.ReadAllText(Menu);
        Assert.Contains("Exec=\"/home/sam/Applications/Tinysnap.AppImage\" %F", text);
        Assert.Contains("MimeType=image/png;image/jpeg;", text);
        Assert.Contains("StartupWMClass=Tinysnap", text);
        Assert.DoesNotContain("\r", text);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Icon));

        entries.Write("/home/sam/Tinysnap.AppImage", [1, 2, 3]);
        Assert.Contains("Exec=\"/home/sam/Tinysnap.AppImage\" %F", File.ReadAllText(Menu));
    }

    [Fact]
    public void APathWithSpacesAndSignsStillLaunches()
    {
        Entries().Write("/home/sam/My Apps/50% \"off\" $HOME`x`\\Tinysnap.AppImage", []);
        // Quoted per the desktop entry spec: \" \` \$ \\ inside the quotes, each backslash
        // doubled again as a string value, and % as %%.
        Assert.Contains("Exec=\"/home/sam/My Apps/50%% \\\\\"off\\\\\" \\\\$HOME\\\\`x\\\\`\\\\\\\\Tinysnap.AppImage\" %F", File.ReadAllText(Menu));
    }

    [Fact]
    public void StartAtLoginIsAnAutostartEntry()
    {
        var entries = Entries();
        entries.Write("/opt/Tinysnap.AppImage", []);
        Assert.False(entries.IsEnabled);
        Assert.True(entries.SetEnabled(true));
        Assert.True(entries.IsEnabled);
        Assert.Contains("Exec=\"/opt/Tinysnap.AppImage\"\n", File.ReadAllText(Autostart));
        Assert.True(entries.SetEnabled(false));
        Assert.False(File.Exists(Autostart));
    }

    [Fact]
    public void AMovedAppImageMovesItsAutostartEntryToo()
    {
        var entries = Entries();
        entries.Write("/opt/Tinysnap.AppImage", []);
        entries.SetEnabled(true);
        Entries().Write("/home/sam/Tinysnap.AppImage", []);
        Assert.Contains("Exec=\"/home/sam/Tinysnap.AppImage\"\n", File.ReadAllText(Autostart));
    }

    [Fact]
    public void TurningOffWhatWasNeverOnSucceeds()
    {
        Assert.True(Entries().SetEnabled(false));
    }

    [Fact]
    public void RemovingLeavesNoEntryIconOrAutostart()
    {
        var entries = Entries();
        entries.Write("/opt/Tinysnap.AppImage", [1]);
        entries.SetEnabled(true);
        entries.Remove();
        Assert.False(File.Exists(Menu));
        Assert.False(File.Exists(Autostart));
        Assert.False(File.Exists(Icon));
    }

    [Fact]
    public void RemovingWhatWasNeverWrittenSucceeds()
    {
        Entries().Remove();
        Assert.False(File.Exists(Menu));
    }
}
