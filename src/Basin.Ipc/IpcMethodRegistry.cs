using System.Text.Json;
using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

public sealed class IpcMethodRegistry
{
    private readonly Dictionary<string, IpcHandler> _methods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IpcMethodInfo> _info = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _schemaUtf8 = new(StringComparer.Ordinal);
    private readonly List<IpcLineForm> _lines = [];
    private readonly HashSet<string> _omitted = new(StringComparer.Ordinal);
    private readonly List<string> _omittedGroups = [];
    private string[]? _sorted;

    public bool IsFrozen { get; private set; }

    public int Count => _methods.Count;

    public IReadOnlyList<string> Names => SortedNames;

    internal string[] SortedNames => _sorted ??= [.. _methods.Keys.Order(StringComparer.Ordinal)];

    internal IpcMethodDetail[]? Details { get; set; }

    internal IReadOnlyList<IpcLineForm> Lines => _lines;

    public void Register(
        string name, IpcHandler handler, string? line = null, IpcLineFormatter? lineReply = null, IpcMethodInfo? info = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (IsReserved(name))
        {
            throw new ArgumentException($"'{name}' is in a namespace the library owns", nameof(name));
        }

        Add(name, handler, info);
        if (line is not null)
        {
            AddLine(name, line, lineReply);
        }
    }

    public void AddLine(string method, string pattern, IpcLineFormatter? reply = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ThrowIfFrozen();
        if (!IpcProtocol.IsValidName(method))
        {
            throw new ArgumentException($"'{method}' is not a method name", nameof(method));
        }

        _lines.Add(new IpcLineForm(method, IpcLinePattern.Parse(pattern), reply));
    }

    public bool Contains(string name) => _methods.ContainsKey(name);

    public bool TryGetInfo(string name, out IpcMethodInfo info)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_info.TryGetValue(name, out info!))
        {
            return true;
        }

        if (_methods.ContainsKey(name) && IsReserved(name))
        {
            info = IpcSchemas.Of(name);
            if (info.ParamsSchema is { } schema)
            {
                info = info with { ParamsSchema = Compact(name, schema) };
            }

            _info[name] = info;
            return true;
        }

        return false;
    }

    internal ReadOnlySpan<byte> SchemaUtf8(string name) => SchemaMemory(name).Span;

    internal ReadOnlyMemory<byte> SchemaMemory(string name)
    {
        if (_schemaUtf8.TryGetValue(name, out var cached))
        {
            return cached;
        }

        if (!TryGetInfo(name, out var info) || info.ParamsSchema is not { } schema)
        {
            return default;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(schema);
        _schemaUtf8[name] = bytes;
        return bytes;
    }

    public string? LineOf(string name)
    {
        foreach (var line in _lines)
        {
            if (line.Method == name)
            {
                return line.Pattern.Text;
            }
        }

        return null;
    }

    public bool TryInvoke(string name, ReadOnlySpan<byte> parameters, IpcReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (name is null || !_methods.TryGetValue(name, out var handler))
        {
            return false;
        }

        var bound = new IpcParams(parameters, reply);
        IpcParamsReuse.Enter();
        try
        {
            handler(ref bound, reply);
            if (!reply.IsDeferred && !reply.ResultIsBalanced)
            {
                reply.Fail(IpcErrorCodes.Internal, "the handler left its result unfinished");
            }
        }
        catch (Exception exception)
        {
            Log.Error($"{name} threw: {exception}");
            if (!reply.IsDeferred)
            {
                reply.Fail(IpcErrorCodes.Internal, exception.Message);
            }
        }
        finally
        {
            IpcParamsReuse.Exit();
        }

        return true;
    }

    internal void RegisterLibrary(string name, IpcHandler handler)
    {
        if (!IsOmitted(name))
        {
            Add(name, handler, null);
        }
    }

    public bool IsOmitted(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_omitted.Contains(name))
        {
            return true;
        }

        var space = IpcProtocol.NamespaceOf(name);
        foreach (var group in _omittedGroups)
        {
            if (space.SequenceEqual(group))
            {
                return true;
            }
        }

        return false;
    }

    internal void Omit(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        ThrowIfFrozen();
        if (name.EndsWith("/*", StringComparison.Ordinal) && IpcProtocol.IsValidName(name[..^1] + "x"))
        {
            _omittedGroups.Add(name[..^2]);
            return;
        }

        if (!IpcProtocol.IsValidName(name))
        {
            throw new ArgumentException($"'{name}' is neither a method name nor group/*", nameof(name));
        }

        _omitted.Add(name);
    }

    internal void Freeze() => IsFrozen = true;

    internal static bool IsReserved(string name)
    {
        var space = IpcProtocol.NamespaceOf(name);
        foreach (var reserved in IpcMethodNames.ReservedNamespaces)
        {
            if (space.SequenceEqual(reserved))
            {
                return true;
            }
        }

        return false;
    }

    private void Add(string name, IpcHandler handler, IpcMethodInfo? info)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfFrozen();
        if (!IpcProtocol.IsValidName(name))
        {
            throw new ArgumentException($"'{name}' is not namespace/verb in [a-z0-9-]", nameof(name));
        }

        if (info?.ParamsSchema is { } schema)
        {
            info = info with { ParamsSchema = Compact(name, schema) };
        }

        if (!_methods.TryAdd(name, handler))
        {
            throw new InvalidOperationException($"'{name}' is already registered");
        }

        if (info is not null)
        {
            _info[name] = info;
        }

        _sorted = null;
    }

    private static string Compact(string name, string schema)
    {
        try
        {
            using var document = JsonDocument.Parse(schema);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.ValueEquals("object"u8))
            {
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                {
                    root.WriteTo(writer);
                }

                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"the schema of '{name}' is not JSON: {exception.Message}", nameof(schema));
        }

        throw new ArgumentException($"the schema of '{name}' is not an object with \"type\": \"object\"", nameof(schema));
    }

    private void ThrowIfFrozen()
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException("methods register before the server starts");
        }
    }
}
