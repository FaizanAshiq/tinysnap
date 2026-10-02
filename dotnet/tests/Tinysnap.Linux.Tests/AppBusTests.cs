using System.Diagnostics;
using Tinysnap.Core;

namespace Tinysnap.Linux.Tests;

public class AppBusTests
{
    private static string Unique() => $"com.faizanashiq.TinysnapTest{Guid.NewGuid():N}";

    [Fact]
    public async Task GnomesShortcutCommandReachesTheRunningCopy()
    {
        Linux.Only();
        var name = Unique();
        using var bus = (await AppBus.TryOwn(Linux.SessionBus, name))!;
        var performed = new TaskCompletionSource<HotKeyAction>();
        bus.Performed += action => performed.TrySetResult(action);
        // Exactly as GNOME runs it.
        using var gdbus = Process.Start("sh", ["-c", AppBus.Command(name, HotKeyAction.RepeatArea)])!;
        Assert.Equal(HotKeyAction.RepeatArea, await performed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OnlyOneCopyOwnsTheNameAndASecondHandsOverItsFiles()
    {
        Linux.Only();
        var name = Unique();
        using var first = (await AppBus.TryOwn(Linux.SessionBus, name))!;
        Assert.Null(await AppBus.TryOwn(Linux.SessionBus, name));
        var opened = new TaskCompletionSource<IReadOnlyList<string>>();
        first.Opened += files => opened.TrySetResult(files);
        await AppBus.HandOver(Linux.SessionBus, ["Receipt.png"], name);
        var files = await opened.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal([Path.GetFullPath("Receipt.png")], files);
    }

    [Fact]
    public void TheShortcutCommandNamesTheActionAsTheWireDoes()
    {
        Assert.Equal("gdbus call --session --dest com.faizanashiq.Tinysnap --object-path /com/faizanashiq/Tinysnap " +
                     "--method com.faizanashiq.Tinysnap.Perform window",
                     AppBus.Command(AppBus.Name, HotKeyAction.Window));
    }
}
