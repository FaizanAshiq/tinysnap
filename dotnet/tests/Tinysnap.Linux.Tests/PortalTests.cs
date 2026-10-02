using System.Diagnostics;
using Tmds.DBus.Protocol;

namespace Tinysnap.Linux.Tests;

public class PortalTests
{
    [Fact]
    public async Task TheAnsweredFileComesBack()
    {
        Linux.Only();
        var (portal, name, owner) = await FakePortal.Start(0, "file:///tmp/Screenshot%20from%20today.png");
        using var _ = owner;
        using var client = await FakePortal.Client();
        Assert.Equal("/tmp/Screenshot from today.png", await Portal.Screenshot(client, interactive: true, TimeSpan.FromSeconds(10), name));
        Assert.Equal([true], portal.Interactive);
    }

    [Fact]
    public async Task ARefusalReadsAsNothing()
    {
        Linux.Only();
        var (_, name, owner) = await FakePortal.Start(1);
        using var _ = owner;
        using var client = await FakePortal.Client();
        Assert.Null(await Portal.Screenshot(client, interactive: false, TimeSpan.FromSeconds(10), name));
    }

    [Fact]
    public async Task APortalThatNeverAnswersIsGivenUpOn()
    {
        Linux.Only();
        var (_, name, owner) = await FakePortal.Start(null);
        using var _ = owner;
        using var client = await FakePortal.Client();
        var clock = Stopwatch.StartNew();
        Assert.Null(await Portal.Screenshot(client, interactive: false, TimeSpan.FromSeconds(1), name));
        Assert.InRange(clock.Elapsed.TotalSeconds, 0.9, 5);
    }

    [Fact]
    public async Task TheAppsOwnConnectionCanAskThePortal()
    {
        // .NET's shared session connection cannot name itself, which the answer is addressed by:
        // asking through it crashed Tinysnap on its first Wayland capture.
        Linux.Only();
        var (_, name, owner) = await FakePortal.Start(0);
        using var _ = owner;
        var connection = Portal.Connection(Linux.SessionBus);
        Assert.Equal("/tmp/shot.png", await Portal.Screenshot(connection(), interactive: false, TimeSpan.FromSeconds(10), name));
        Assert.Same(connection(), connection());
    }

    [Fact]
    public async Task NoPortalAtAllReadsAsNothing()
    {
        Linux.Only();
        using var client = await FakePortal.Client();
        Assert.Null(await Portal.Screenshot(client, interactive: false, TimeSpan.FromSeconds(5), $"com.faizanashiq.Nobody{Guid.NewGuid():N}"));
    }
}
