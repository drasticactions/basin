using Tmds.DBus.Protocol;

namespace Basin.Tests;

internal sealed class PortalTestClient : IDisposable
{
    public PortalTestClient(DBusConnection connection, string destination)
    {
        Connection = connection;
        Destination = destination;
    }

    public DBusConnection Connection { get; }

    public string Destination { get; }

    public static async Task<PortalTestClient> ConnectAsync(string address, string destination, bool asFrontend)
    {
        var connection = new DBusConnection(address);
        await connection.ConnectAsync();
        if (asFrontend)
        {
            _ = await connection.TryRequestNameAsync(Basin.Portal.PortalBus.FrontendName, RequestNameOptions.None);
        }

        return new PortalTestClient(connection, destination);
    }

    public Task<string> IntrospectAsync(string path)
    {
        using var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, path, "org.freedesktop.DBus.Introspectable", "Introspect");
        return Connection.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) => m.GetBodyReader().ReadString(), null);
    }

    public Task<VariantValue> GetPropertyAsync(string path, string iface, string property)
    {
        using var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, path, "org.freedesktop.DBus.Properties", "Get", "ss");
        writer.WriteString(iface);
        writer.WriteString(property);
        return Connection.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) => m.GetBodyReader().ReadVariantValue(), null);
    }

    public Task CallAsync(string path, string iface, string member)
    {
        using var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, path, iface, member);
        return Connection.CallMethodAsync(writer.CreateMessage());
    }

    private int _requests;

    public ObjectPath NextRequest() => new(Basin.Portal.PortalBus.RootPath + "/request/1_1/r" + (++_requests));

    public Task<(uint Response, Dictionary<string, VariantValue> Results)> CallScreenshotAsync(
        string appId, string parentWindow, Dictionary<string, VariantValue> options, ObjectPath? handle = null) =>
        CallImplAsync("org.freedesktop.impl.portal.Screenshot", "Screenshot", appId, parentWindow, options, handle);

    public Task<(uint Response, Dictionary<string, VariantValue> Results)> CallSessionImplAsync(
        string iface, string member, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options, string? parentWindow = null, ObjectPath? handle = null)
    {
        using var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, Basin.Portal.PortalBus.RootPath, iface, member, parentWindow is null ? "oosa{sv}" : "oossa{sv}");
        writer.WriteObjectPath(handle ?? NextRequest());
        writer.WriteObjectPath(sessionHandle);
        writer.WriteString(appId);
        if (parentWindow is not null)
        {
            writer.WriteString(parentWindow);
        }

        writer.WriteDictionary(options);
        return Connection.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) => ReadResponse(m), null);
    }

    public Task<(uint Response, Dictionary<string, VariantValue> Results)> CallImplAsync(
        string iface, string member, string appId, string parentWindow, Dictionary<string, VariantValue> options, ObjectPath? handle = null)
    {
        using var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, Basin.Portal.PortalBus.RootPath, iface, member, "ossa{sv}");
        writer.WriteObjectPath(handle ?? NextRequest());
        writer.WriteString(appId);
        writer.WriteString(parentWindow);
        writer.WriteDictionary(options);
        return Connection.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) => ReadResponse(m), null);
    }

    public delegate void WriteBody(ref MessageWriter writer);

    public Task CallRawAsync(string iface, string member, string signature, WriteBody write)
    {
        var writer = Connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(Destination, Basin.Portal.PortalBus.RootPath, iface, member, signature);
            write(ref writer);
            return Connection.CallMethodAsync(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    public Task<T> CallRawAsync<T>(string iface, string member, string signature, WriteBody write, MessageValueReader<T> read)
    {
        var writer = Connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(Destination, Basin.Portal.PortalBus.RootPath, iface, member, signature);
            write(ref writer);
            return Connection.CallMethodAsync(writer.CreateMessage(), read, null);
        }
        finally
        {
            writer.Dispose();
        }
    }

    public static (uint Response, Dictionary<string, VariantValue> Results) ReadResponse(Message message)
    {
        var reader = message.GetBodyReader();
        var response = reader.ReadUInt32();
        var results = reader.ReadDictionaryOfStringToVariantValue();
        return (response, results);
    }

    public void Dispose() => Connection.Dispose();
}

internal static class PortalClientSupport
{
    public static ObjectPath RequestPath(DBusConnection connection, string token)
    {
        var sender = connection.UniqueName ?? throw new InvalidOperationException("the client has no unique name");
        return new ObjectPath(Basin.Portal.PortalBus.RootPath + "/request/" + sender.TrimStart(':').Replace('.', '_') + "/" + token);
    }

    public static ObjectPath SessionPath(DBusConnection connection, string token)
    {
        var sender = connection.UniqueName ?? throw new InvalidOperationException("the client has no unique name");
        return new ObjectPath(Basin.Portal.PortalBus.RootPath + "/session/" + sender.TrimStart(':').Replace('.', '_') + "/" + token);
    }
}

internal static class PortalClientRequests
{
    private static int _tokens;

    public static (uint Response, Dictionary<string, VariantValue> Results) Run(
        CompositorTestHost host,
        DBusConnection connection,
        Func<Dictionary<string, VariantValue>, Task<ObjectPath>> call,
        Dictionary<string, VariantValue>? options = null,
        int rounds = 1500)
    {
        var token = "t" + Interlocked.Increment(ref _tokens);
        var requestPath = PortalClientSupport.RequestPath(connection, token);
        var request = new Basin.Tests.PortalClient.Request(connection, Basin.Portal.PortalBus.FrontendName, requestPath);
        (uint Response, Dictionary<string, VariantValue> Results)? reply = null;
        using var watch = PortalBusTests.Await(host, request.WatchResponseAsync(r => reply = r, emitOnCapturedContext: false).AsTask());
        var merged = new Dictionary<string, VariantValue>(options ?? []) { ["handle_token"] = VariantValue.String(token) };
        var handle = PortalBusTests.Await(host, call(merged));
        Xunit.Assert.Equal(requestPath, handle);
        PortalBusTests.PumpUntil(host, () => reply is not null, rounds);
        return reply!.Value;
    }

    public static ObjectPath SessionToken(DBusConnection connection, Dictionary<string, VariantValue> options)
    {
        var token = "s" + Interlocked.Increment(ref _tokens);
        options["session_handle_token"] = VariantValue.String(token);
        return PortalClientSupport.SessionPath(connection, token);
    }
}
