using Tmds.DBus.Protocol;
using Tinysnap.Core;

namespace Tinysnap.Linux;

/// <summary>Tinysnap's own name on the session bus. The running copy owns it and answers
/// <c>Perform</c>, which GNOME's shortcuts call, and <c>Open</c>, which a second launch calls with
/// its files before it quits. Owning the name is what keeps Tinysnap to one copy.</summary>
internal sealed class AppBus : IPathMethodHandler, IDisposable
{
    public const string Name = "com.faizanashiq.Tinysnap";
    public const string ObjectPath = "/com/faizanashiq/Tinysnap";
    private const string Interface = "com.faizanashiq.Tinysnap";

    private static readonly ReadOnlyMemory<byte> Introspection =
        """
        <interface name="com.faizanashiq.Tinysnap">
          <method name="Perform"><arg name="action" type="s" direction="in"/></method>
          <method name="Open"><arg name="files" type="as" direction="in"/></method>
        </interface>
        """u8.ToArray();

    private readonly DBusConnection connection;

    private AppBus(DBusConnection connection) => this.connection = connection;

    /// <summary>A hotkey's action, raised on a bus thread.</summary>
    public event Action<HotKeyAction>? Performed;

    /// <summary>Tinysnap opened again, with these files or none, raised on a bus thread.</summary>
    public event Action<IReadOnlyList<string>>? Opened;

    public string Path => ObjectPath;

    public bool HandlesChildPaths => false;

    /// <summary>The command a GNOME shortcut runs for <paramref name="action"/>.</summary>
    public static string Command(string busName, HotKeyAction action) =>
        $"gdbus call --session --dest {busName} --object-path {ObjectPath} --method {Interface}.Perform {Json.Wire(action)}";

    /// <summary>Owns <paramref name="name"/> and answers on it; null when another copy already does.</summary>
    public static async Task<AppBus?> TryOwn(string address, string name = Name)
    {
        var connection = new DBusConnection(address);
        await connection.ConnectAsync();
        var bus = new AppBus(connection);
        connection.AddMethodHandler(bus);
        if (await connection.TryRequestNameAsync(name, RequestNameOptions.None)) return bus;
        connection.Dispose();
        return null;
    }

    /// <summary>For a later copy: hands the running one its files, as full paths.</summary>
    public static async Task HandOver(string address, IReadOnlyList<string> files, string name = Name)
    {
        using var connection = new DBusConnection(address);
        await connection.ConnectAsync();
        MessageBuffer message;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(name, ObjectPath, Interface, "Open", "as", MessageFlags.None);
            writer.WriteArray(files.Select(System.IO.Path.GetFullPath).ToArray());
            message = writer.CreateMessage();
        }
        await connection.CallMethodAsync(message);
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([Introspection], []);
            return ValueTask.CompletedTask;
        }
        var request = context.Request;
        switch (request.MemberAsString, request.SignatureAsString)
        {
            case ("Perform", "s"):
                var name = request.GetBodyReader().ReadString();
                if (Json.FromWire<HotKeyAction>(name) is not { } action)
                {
                    context.ReplyError($"{Interface}.UnknownAction", $"No action named {name}");
                    break;
                }
                ReplyEmpty(context);
                Performed?.Invoke(action);
                break;
            case ("Open", "as"):
                var files = request.GetBodyReader().ReadArrayOfString();
                ReplyEmpty(context);
                Opened?.Invoke(files);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
        return ValueTask.CompletedTask;
    }

    private static void ReplyEmpty(MethodContext context)
    {
        if (context.NoReplyExpected) return;
        using var writer = context.CreateReplyWriter(null!);
        context.Reply(writer.CreateMessage());
    }

    public void Dispose() => connection.Dispose();
}
