using System.IO.Pipes;

namespace Tinysnap.Windows;

/// <summary>One Tinysnap per sign-in. A second copy finds the first by a named mutex, hands it the
/// files it was opened with through a named pipe only the same user can reach, and quits; two
/// copies would fight over the hotkeys.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly string name;
    private readonly Mutex running;
    private readonly CancellationTokenSource stopping = new();

    /// <summary>Another copy was started, with these files or none, and has quit. Raised on a
    /// thread of its own.</summary>
    public event Action<IReadOnlyList<string>>? Reopened;

    public bool IsFirst { get; }

    /// <param name="name">Tinysnap's own; tests pass one of their own.</param>
    public SingleInstance(string name = "Tinysnap")
    {
        this.name = $"{name}.{Environment.UserName}";
        running = new Mutex(true, $@"Local\{this.name}.Running", out var createdNew);
        IsFirst = createdNew;
        if (IsFirst) _ = Listen();
    }

    private string PipeName => $"{name}.Reopen";

    /// <summary>Each later copy connects, sends its files one a line, and closes.</summary>
    private async Task Listen()
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                                                                 PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopping.Token);
                using var reader = new StreamReader(pipe);
                var files = new List<string>();
                while (await reader.ReadLineAsync(stopping.Token) is { } line)
                    if (line.Length > 0) files.Add(line);
                Reopened?.Invoke(files);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A copy that went away mid message; wait for the next.
            }
        }
    }

    /// <summary>For a later copy: hands the first its files, as full paths, and brings it forward.</summary>
    public void AskFirstToReopen(IReadOnlyList<string> files)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe);
            foreach (var file in files) writer.WriteLine(Path.GetFullPath(file));
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // The first copy is closing or stuck; this one quits all the same.
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
        if (IsFirst) running.ReleaseMutex();
        running.Dispose();
    }
}
