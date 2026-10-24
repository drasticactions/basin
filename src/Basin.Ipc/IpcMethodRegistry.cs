using System.Text.Json;
using static Basin.Ipc.IpcLog;

namespace Basin.Ipc;

/// <summary>
/// Maps method names to handlers, and holds the line forms and the metadata of the methods. It is frozen when the server starts.
/// </summary>
public sealed class IpcMethodRegistry
{
    private readonly Dictionary<string, IpcHandler> _methods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IpcMethodInfo> _info = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _schemaUtf8 = new(StringComparer.Ordinal);
    private readonly List<IpcLineForm> _lines = [];
    private readonly HashSet<string> _omitted = new(StringComparer.Ordinal);
    private readonly List<string> _omittedGroups = [];
    private string[]? _sorted;

    /// <summary>
    /// Gets a value indicating whether the server started, after which no method or line form can be added.
    /// </summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Gets the number of registered methods, library methods included.
    /// </summary>
    public int Count => _methods.Count;

    /// <summary>
    /// Gets the registered method names in ordinal order.
    /// </summary>
    public IReadOnlyList<string> Names => SortedNames;

    internal string[] SortedNames => _sorted ??= [.. _methods.Keys.Order(StringComparer.Ordinal)];

    internal IpcMethodDetail[]? Details { get; set; }

    internal IReadOnlyList<IpcLineForm> Lines => _lines;

    /// <summary>
    /// Registers a consumer method, and optionally its line form and its metadata.
    /// </summary>
    /// <param name="name">The method name, <c>namespace/verb</c> in <c>[a-z0-9-]</c>, outside the library namespaces.</param>
    /// <param name="handler">The handler.</param>
    /// <param name="line">The line form pattern for the line front, or <see langword="null"/> for none.</param>
    /// <param name="lineReply">The formatter that prints the reply on the line front, or <see langword="null"/> for the default.</param>
    /// <param name="info">The metadata, or <see langword="null"/>. Its schema must parse as an object with <c>"type": "object"</c>.</param>
    /// <exception cref="ArgumentException">The name is not valid or is in a library namespace, or the schema is not valid.</exception>
    /// <exception cref="InvalidOperationException">The name is already registered, or the server started.</exception>
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

    /// <summary>
    /// Adds a line form for a method. A method can have several line forms, and the line front tries them in order.
    /// </summary>
    /// <param name="method">The method name. It does not need to be registered yet.</param>
    /// <param name="pattern">The pattern, for example <c>move {x:number} {y:number}</c>.</param>
    /// <param name="reply">The formatter that prints the reply, or <see langword="null"/> for the default.</param>
    /// <exception cref="ArgumentException">The method name is not valid.</exception>
    /// <exception cref="InvalidOperationException">The server started.</exception>
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

    /// <summary>
    /// Tells whether a method is registered.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <returns><see langword="true"/> when the method is in the table.</returns>
    public bool Contains(string name) => _methods.ContainsKey(name);

    internal bool HasLine(string method)
    {
        foreach (var form in _lines)
        {
            if (form.Method == method)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the metadata of a method. A library method gets its metadata on the first request.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <param name="info">The metadata with its schema compacted, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the method has no metadata or is not registered.</returns>
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

    /// <summary>
    /// Finds the pattern of the first line form of a method.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <returns>The pattern text, or <see langword="null"/> when the method has no line form.</returns>
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

    /// <summary>
    /// Runs a handler with no interceptor. An exception from the handler becomes an <c>internal</c> error, and so does a result left unfinished.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <param name="parameters">The raw JSON of the params.</param>
    /// <param name="reply">The reply that the handler fills.</param>
    /// <returns><see langword="false"/> when the method is not registered. The reply is then unchanged.</returns>
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

    internal void Wrap(string name, Func<IpcHandler, IpcHandler> wrap)
    {
        if (_methods.TryGetValue(name, out var handler))
        {
            _methods[name] = wrap(handler);
        }
    }

        /// <summary>
        /// Tells whether <see cref="IpcServer.Omit"/> left a library method out, by its name or its group.
        /// </summary>
        /// <param name="name">The method name.</param>
        /// <returns><see langword="true"/> when the method or its group is omitted.</returns>
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
