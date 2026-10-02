using Tmds.DBus.Protocol;

namespace Tinysnap.Linux;

/// <summary>The desktop portal's Screenshot: Tinysnap asks, GNOME answers through a Request object's
/// Response signal, which is subscribed before asking, on the path the portal will use, so the
/// answer cannot be missed. GNOME asks the person once whether Tinysnap may take screenshots.</summary>
internal static class Portal
{
    private const string Destination = "org.freedesktop.portal.Desktop";
    private const string Path = "/org/freedesktop/portal/desktop";
    private const string Request = "org.freedesktop.portal.Request";

    /// <summary>The screenshot's file, or null when refused, cancelled, not answered in time, or
    /// when there is no portal. Interactive opens GNOME's own screenshot tool, where the person
    /// picks a window or an area.</summary>
    public static async Task<string?> Screenshot(DBusConnection connection, bool interactive, TimeSpan timeout,
                                                 string destination = Destination)
    {
        var token = $"tinysnap{Guid.NewGuid():N}";
        var sender = connection.UniqueName!.TrimStart(':').Replace('.', '_');
        var request = $"{Path}/request/{sender}/{token}";
        var answer = new TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var watch = await Watch(connection, destination, request, answer);
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination, Path, "org.freedesktop.portal.Screenshot", "Screenshot", "sa{sv}", MessageFlags.None);
                writer.WriteString("");
                writer.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["handle_token"] = VariantValue.String(token),
                    ["interactive"] = VariantValue.Bool(interactive),
                    ["modal"] = VariantValue.Bool(false),
                });
                message = writer.CreateMessage();
            }
            var handle = await connection.CallMethodAsync(message, (reply, _) => reply.GetBodyReader().ReadObjectPath().ToString(), null);
            // A portal older than handle tokens answers on a path of its own choosing.
            using var other = handle == request ? null : await Watch(connection, destination, handle, answer);
            return await Answered(connection, destination, handle, answer.Task, timeout);
        }
        catch (DBusExceptionBase)
        {
            return null;
        }
    }

    private static async Task<string?> Answered(DBusConnection connection, string destination, string handle,
                                                Task<(uint Code, Dictionary<string, VariantValue> Results)> answer, TimeSpan timeout)
    {
        try
        {
            var (code, results) = await answer.WaitAsync(timeout);
            if (code != 0 || !results.TryGetValue("uri", out var uri)) return null;
            return new Uri(uri.GetString()).LocalPath;
        }
        catch (TimeoutException)
        {
            // Closes GNOME's dialog, if one is still waiting, so it does not answer later.
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(destination, handle, Request, "Close", null, MessageFlags.NoReplyExpected);
            connection.TrySendMessage(writer.CreateMessage());
            return null;
        }
    }

    private static ValueTask<IDisposable> Watch(DBusConnection connection, string destination, string path,
                                                TaskCompletionSource<(uint Code, Dictionary<string, VariantValue> Results)> answer) =>
        connection.WatchSignalAsync(destination, path, Request, "Response",
            (message, _) =>
            {
                var reader = message.GetBodyReader();
                return (reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue());
            },
            notification =>
            {
                if (notification.HasValue) answer.TrySetResult(notification.Value);
                else if (notification.Exception is { } error) answer.TrySetException(error);
            },
            ObserverFlags.None, false, null);
}
