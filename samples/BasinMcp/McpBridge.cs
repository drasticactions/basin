using System.Text.Json;
using Basin.Ipc;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using static BasinMcp.McpLog;

namespace BasinMcp;

internal sealed class McpBridge : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly McpToolSignal _signal = new();
    private readonly Lock _publishGate = new();
    private BasinIpcClient? _client;
    private McpToolTable _table = McpToolTable.Empty;
    private IpcVersion? _version;
    private string[]? _published;
    private string? _lastAttempt;
    private string? _lastFailure;

    private McpServer? _session;

    public McpBridge(McpBridgeOptions options)
    {
        Options = options;
        Relay = new McpApprovalRelay(this);
    }

    public McpApprovalRelay Relay { get; }

    public bool HasConnected { get; private set; }

    public McpBridgeOptions Options { get; }

    public McpToolTable Table => _table;

    public BasinIpcClient? Client => _client;

    public IpcVersion? Version => _version;

    public string? LastAttempt => _lastAttempt;

    public string? LastFailure => _lastFailure;

    public int ListChangedSent => _signal.Raised;

    public McpServerOptions CreateServerOptions() => new()
    {
        ServerInfo = new Implementation { Name = "basin-mcp", Version = typeof(McpBridge).Assembly.GetName().Version?.ToString() ?? "0" },
        Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = true } },
        ToolCollection = _signal,
        Handlers = new McpServerHandlers
        {
            ListToolsHandler = async (request, cancellationToken) =>
            {
                await EnsureRelayAsync(request.Server, cancellationToken).ConfigureAwait(false);
                return await ListToolsAsync(cancellationToken).ConfigureAwait(false);
            },
            CallToolHandler = async (request, cancellationToken) =>
            {
                await EnsureRelayAsync(request.Server, cancellationToken).ConfigureAwait(false);
                Relay.Active = request.Server;
                try
                {
                    return await CallToolAsync(request.Params?.Name ?? string.Empty, request.Params?.Arguments, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Relay.Active = null;
                }
            },
        },
    };

    public async Task<int> RunAsync(ITransport transport, CancellationToken cancellationToken)
    {
        await using var server = McpServer.Create(transport, CreateServerOptions());
        _session = server;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watch = McpSocketWatch.RunAsync(this, stop.Token);
        try
        {
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await watch.ConfigureAwait(false);
        }

        return 0;
    }

    public async Task EnsureRelayAsync(McpServer? server, CancellationToken cancellationToken)
    {
        server ??= _session;
        if (server is not null && McpApprovalRelay.Supports(server) && await TryConnectAsync(cancellationToken).ConfigureAwait(false))
        {
            await Relay.EnsureAsync(server, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<ListToolsResult> ListToolsAsync(CancellationToken cancellationToken)
    {
        _ = await TryConnectAsync(cancellationToken).ConfigureAwait(false);
        var table = _table;
        lock (_publishGate)
        {
            _published = [.. table.Methods];
        }

        var result = new ListToolsResult();
        foreach (var entry in table.Entries.OrderBy(entry => entry.Tool.Name, StringComparer.Ordinal))
        {
            result.Tools.Add(entry.Tool);
        }

        result.Tools.Add(McpWaitEvent.Tool(table.Events, Options.Filter.ReadOnly));
        result.Tools.Add(McpBridgeStatus.Tool);
        Log.Debug($"tools/list: {result.Tools.Count} tools");
        return result;
    }

    public async ValueTask<CallToolResult> CallToolAsync(
        string name, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        Log.Debug($"tools/call {name}");
        if (name == McpBridgeStatus.Name)
        {
            _ = await TryConnectAsync(cancellationToken).ConfigureAwait(false);
            return McpBridgeStatus.Result(this);
        }

        if (!await TryConnectAsync(cancellationToken).ConfigureAwait(false))
        {
            return McpCallMapper.Unavailable(_lastFailure ?? "not connected");
        }

        if (name == McpWaitEvent.Name)
        {
            try
            {
                return await McpWaitEvent.RunAsync(this, arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                return McpCallMapper.Unavailable(exception.Message);
            }
        }

        for (var attempt = 0; ; attempt++)
        {
            if (!_table.TryGet(name, out var entry))
            {
                throw new McpProtocolException($"Unknown tool: '{name}'", McpErrorCode.InvalidParams);
            }

            var client = _client;
            try
            {
                return await InvokeAsync(client!, entry, arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (IpcCallException exception)
            {
                return McpCallMapper.Error(exception);
            }
            catch (Exception exception) when (attempt == 0 && IsConnectionFailure(exception))
            {
                Log.Info($"the control connection dropped during {entry.Method}; reconnecting");
                if (!await TryConnectAsync(cancellationToken).ConfigureAwait(false))
                {
                    return McpCallMapper.Unavailable(_lastFailure ?? exception.Message);
                }
            }
            catch (Exception exception) when (IsConnectionFailure(exception))
            {
                return McpCallMapper.Unavailable(exception.Message);
            }
        }
    }

    public async Task<CallToolResult> InvokeAsync(
        BasinIpcClient client, McpToolEntry entry, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        if (entry.Kind == McpToolKind.Capture)
        {
            var captured = await client.CallAsync(
                entry.Method, writer => McpCaptureTool.WriteParams(writer, arguments, Options.MaxDimension), cancellationToken).ConfigureAwait(false);
            return McpCaptureTool.Result(captured);
        }

        var result = await client.CallAsync(
            entry.Method, writer => McpCallMapper.WriteArguments(writer, arguments), cancellationToken).ConfigureAwait(false);
        return McpCallMapper.Result(result);
    }

    public async Task<bool> TryConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is { IsConnected: true })
        {
            return true;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is { IsConnected: true })
            {
                return true;
            }

            if (_client is { } dead)
            {
                _client = null;
                await dead.DisposeAsync().ConfigureAwait(false);
            }

            _lastAttempt = BasinIpcClient.ResolvePath(Options.SocketPath);
            BasinIpcClient? client = null;
            try
            {
                client = await BasinIpcClient.ConnectAsync(Options.SocketPath, cancellationToken).ConfigureAwait(false);
                var version = await client.VersionAsync(cancellationToken).ConfigureAwait(false);
                var methods = await client.MethodsDetailAsync(cancellationToken).ConfigureAwait(false);
                var events = await client.EventsAsync(cancellationToken).ConfigureAwait(false);
                _table = McpToolTable.Build(methods, events, Options);
                _version = version;
                _client = client;
                _lastFailure = null;
                HasConnected = true;
                Log.Info($"connected to {version.Compositor} on {client.Path}: {_table.Methods.Count} tools");
            }
            catch (Exception exception) when (IsConnectionFailure(exception) || exception is IpcCallException)
            {
                if (client is not null)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }

                _table = McpToolTable.Empty;
                _version = null;
                _lastFailure = exception.Message;
                Log.Debug($"no compositor: {exception.Message}");
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }

        NotifyIfChanged();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await Relay.DisposeAsync().ConfigureAwait(false);
        if (_client is { } client)
        {
            _client = null;
            await client.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }

    internal static bool IsConnectionFailure(Exception exception) =>
        exception is IOException or ObjectDisposedException or System.Net.Sockets.SocketException;

    private void NotifyIfChanged()
    {
        var methods = _table.Methods;
        lock (_publishGate)
        {
            if (_published is null || _published.SequenceEqual(methods, StringComparer.Ordinal))
            {
                return;
            }

            _published = [.. methods];
        }

        Log.Info($"the method set changed; sending tools/list_changed");
        _signal.Signal();
    }
}
