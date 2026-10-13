using Tmds.DBus.Protocol;

namespace Basin.Tests;

internal sealed class MockSystemService : IPathMethodHandler, IDisposable
{
    private readonly object _lock = new();
    private readonly DBusConnection _connection;
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, VariantValue>>> _objects = new(StringComparer.Ordinal);
    private readonly List<(string Path, string Interface, string Member, VariantValue[] Arguments)> _calls = [];

    private MockSystemService(DBusConnection connection)
    {
        _connection = connection;
    }

    public string Path => "/";

    public bool HandlesChildPaths => true;

    public Func<MethodContext, bool>? OnMethod { get; set; }

    public static async Task<MockSystemService> StartAsync(string address, string name)
    {
        var connection = new DBusConnection(address);
        await connection.ConnectAsync();
        var service = new MockSystemService(connection);
        connection.AddMethodHandler(service);
        await connection.RequestNameAsync(name, RequestNameOptions.None);
        return service;
    }

    public void Add(string path, string @interface, Dictionary<string, VariantValue> properties)
    {
        lock (_lock)
        {
            if (!_objects.TryGetValue(path, out var interfaces))
            {
                _objects[path] = interfaces = new Dictionary<string, Dictionary<string, VariantValue>>(StringComparer.Ordinal);
            }

            interfaces[@interface] = new Dictionary<string, VariantValue>(properties, StringComparer.Ordinal);
        }
    }

    public VariantValue Property(string path, string @interface, string name)
    {
        lock (_lock)
        {
            return _objects[path][@interface][name];
        }
    }

    public void Set(string path, string @interface, string name, VariantValue value, bool emit = true)
    {
        lock (_lock)
        {
            _objects[path][@interface][name] = value;
        }

        if (emit)
        {
            using var writer = _connection.GetMessageWriter();
            writer.WriteSignalHeader(null, path, "org.freedesktop.DBus.Properties", "PropertiesChanged", "sa{sv}as");
            writer.WriteString(@interface);
            var changed = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString(name);
            writer.WriteVariant(value);
            writer.WriteDictionaryEnd(changed);
            writer.WriteArray(Array.Empty<string>());
            _ = _connection.TrySendMessage(writer.CreateMessage());
        }
    }

    public void Signal(string path, string @interface, string member, params uint[] values)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(null, path, @interface, member, new string('u', values.Length));
        foreach (var value in values)
        {
            writer.WriteUInt32(value);
        }

        _ = _connection.TrySendMessage(writer.CreateMessage());
    }

    public List<(string Path, string Interface, string Member, VariantValue[] Arguments)> Calls()
    {
        lock (_lock)
        {
            return [.. _calls];
        }
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        var path = request.PathAsString ?? "/";
        var @interface = request.InterfaceAsString ?? "";
        var member = request.MemberAsString ?? "";
        if (@interface == "org.freedesktop.DBus.Properties")
        {
            HandleProperties(context, path, member);
            return ValueTask.CompletedTask;
        }

        if (@interface == "org.freedesktop.DBus.ObjectManager" && member == "GetManagedObjects")
        {
            ReplyManagedObjects(context);
            return ValueTask.CompletedTask;
        }

        lock (_lock)
        {
            _calls.Add((path, @interface, member, ReadArguments(request)));
        }

        if (OnMethod?.Invoke(context) == true)
        {
            return ValueTask.CompletedTask;
        }

        if (!context.NoReplyExpected)
        {
            using var writer = context.CreateReplyWriter(null);
            context.Reply(writer.CreateMessage());
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose() => _connection.Dispose();

    private static VariantValue[] ReadArguments(Message request)
    {
        var signature = request.SignatureAsString ?? "";
        if (signature is "" or "a{sa{sv}}oo")
        {
            return [];
        }

        var reader = request.GetBodyReader();
        var arguments = new List<VariantValue>();
        foreach (var code in signature)
        {
            arguments.Add(code switch
            {
                'o' => VariantValue.ObjectPath(reader.ReadObjectPath()),
                's' => VariantValue.String(reader.ReadString()),
                'b' => VariantValue.Bool(reader.ReadBool()),
                'v' => reader.ReadVariantValue(),
                _ => throw new NotSupportedException(signature),
            });
        }

        return [.. arguments];
    }

    private void HandleProperties(MethodContext context, string path, string member)
    {
        var reader = context.Request.GetBodyReader();
        var @interface = reader.ReadString();
        Dictionary<string, VariantValue>? properties;
        lock (_lock)
        {
            properties = _objects.TryGetValue(path, out var interfaces) && interfaces.TryGetValue(@interface, out var found)
                ? new Dictionary<string, VariantValue>(found, StringComparer.Ordinal)
                : null;
        }

        if (properties is null)
        {
            context.ReplyError("org.freedesktop.DBus.Error.UnknownObject", $"no {@interface} at {path}");
            return;
        }

        switch (member)
        {
            case "GetAll":
                {
                    using var writer = context.CreateReplyWriter("a{sv}");
                    var map = writer.WriteDictionaryStart();
                    foreach (var (key, value) in properties)
                    {
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString(key);
                        writer.WriteVariant(value);
                    }

                    writer.WriteDictionaryEnd(map);
                    context.Reply(writer.CreateMessage());
                    break;
                }

            case "Get":
                {
                    var name = reader.ReadString();
                    if (!properties.TryGetValue(name, out var value))
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", name);
                        return;
                    }

                    using var writer = context.CreateReplyWriter("v");
                    writer.WriteVariant(value);
                    context.Reply(writer.CreateMessage());
                    break;
                }

            case "Set":
                {
                    var name = reader.ReadString();
                    var value = reader.ReadVariantValue();
                    lock (_lock)
                    {
                        _calls.Add((path, @interface, "Set:" + name, [value]));
                    }

                    using (var writer = context.CreateReplyWriter(null))
                    {
                        context.Reply(writer.CreateMessage());
                    }

                    Set(path, @interface, name, value);
                    break;
                }

            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private void ReplyManagedObjects(MethodContext context)
    {
        List<(string Path, List<(string Interface, Dictionary<string, VariantValue> Properties)> Interfaces)> snapshot;
        lock (_lock)
        {
            snapshot = _objects
                .Select(o => (o.Key, o.Value.Select(i => (i.Key, new Dictionary<string, VariantValue>(i.Value))).ToList()))
                .ToList();
        }

        using var writer = context.CreateReplyWriter("a{oa{sa{sv}}}");
        var objects = writer.WriteDictionaryStart();
        foreach (var (path, interfaces) in snapshot)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteObjectPath(path);
            var entries = writer.WriteDictionaryStart();
            foreach (var (name, properties) in interfaces)
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString(name);
                var map = writer.WriteDictionaryStart();
                foreach (var (key, value) in properties)
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString(key);
                    writer.WriteVariant(value);
                }

                writer.WriteDictionaryEnd(map);
            }

            writer.WriteDictionaryEnd(entries);
        }

        writer.WriteDictionaryEnd(objects);
        context.Reply(writer.CreateMessage());
    }
}
