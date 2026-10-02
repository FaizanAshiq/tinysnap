using Tmds.DBus.Protocol;

namespace Tinysnap.Linux.Tests;

/// <summary>Owns a name of its own and answers Screenshot as the portal does: the request's
/// path at once, then its Response signal, or never when <paramref name="code"/> is null.</summary>
internal sealed class FakePortal(DBusConnection connection, uint? code, string uri) : IPathMethodHandler
{
    public string Path => "/org/freedesktop/portal/desktop";

    public bool HandlesChildPaths => false;

    public List<bool> Interactive { get; } = [];

    public static async Task<DBusConnection> Client()
    {
        var connection = new DBusConnection(Linux.SessionBus);
        await connection.ConnectAsync();
        return connection;
    }

    public static async Task<(FakePortal Portal, string Name, DBusConnection Connection)> Start(uint? code, string uri = "file:///tmp/shot.png")
    {
        var connection = new DBusConnection(Linux.SessionBus);
        await connection.ConnectAsync();
        var portal = new FakePortal(connection, code, uri);
        connection.AddMethodHandler(portal);
        var name = $"com.faizanashiq.TinysnapPortal{Guid.NewGuid():N}";
        await connection.RequestNameAsync(name, RequestNameOptions.None);
        return (portal, name, connection);
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        var reader = request.GetBodyReader();
        reader.ReadString();
        var options = reader.ReadDictionaryOfStringToVariantValue();
        Interactive.Add(options["interactive"].GetBool());
        var sender = request.SenderAsString!;
        var handle = $"{Path}/request/{sender.TrimStart(':').Replace('.', '_')}/{options["handle_token"].GetString()}";
        using (var writer = context.CreateReplyWriter("o"))
        {
            writer.WriteObjectPath(handle);
            context.Reply(writer.CreateMessage());
        }
        if (code is not { } answer) return ValueTask.CompletedTask;
        using var signal = connection.GetMessageWriter();
        signal.WriteSignalHeader(sender, handle, "org.freedesktop.portal.Request", "Response", "ua{sv}");
        signal.WriteUInt32(answer);
        signal.WriteDictionary(new Dictionary<string, VariantValue> { ["uri"] = VariantValue.String(uri) });
        connection.TrySendMessage(signal.CreateMessage());
        return ValueTask.CompletedTask;
    }
}
