namespace Tinysnap.Windows;

/// <summary>One Tinysnap per sign-in. A second copy finds the first by a named mutex, asks it to
/// come forward through a named event, and quits; two copies would fight over the hotkeys.</summary>
internal sealed class SingleInstance : IDisposable
{
    private const string Name = @"Local\Tinysnap";

    private readonly Mutex running;
    private readonly EventWaitHandle reopen = new(false, EventResetMode.AutoReset, Name + ".Reopen");

    /// <summary>Another copy was started and has quit; raised on a thread of its own.</summary>
    public event Action? Reopened;

    public bool IsFirst { get; }

    public SingleInstance()
    {
        running = new Mutex(true, Name + ".Running", out var createdNew);
        IsFirst = createdNew;
        if (!IsFirst) return;
        new Thread(() =>
        {
            while (reopen.WaitOne()) Reopened?.Invoke();
        }) { IsBackground = true, Name = "Tinysnap reopen" }.Start();
    }

    /// <summary>For the second copy: tells the first to come forward.</summary>
    public void AskFirstToReopen() => reopen.Set();

    public void Dispose()
    {
        if (IsFirst) running.ReleaseMutex();
        running.Dispose();
    }
}
