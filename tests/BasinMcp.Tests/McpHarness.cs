using System.IO.Pipelines;
using Basin.Tests;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace BasinMcp.Tests;

internal sealed class McpHarness : IDisposable
{
    private readonly Pipe _toServer = new();
    private readonly Pipe _toClient = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _server;
    private int _listChanged;
    private bool _disposed;

    private McpHarness(DirectoryInfo directory, string socketPath, IpcTestRig? rig, McpBridgeOptions options)
    {
        Directory = directory;
        SocketPath = socketPath;
        Rig = rig;
        Bridge = new McpBridge(options with { SocketPath = socketPath });
        var transport = new StreamServerTransport(_toServer.Reader.AsStream(), _toClient.Writer.AsStream(), "basin-mcp-test");
        _server = Task.Run(() => Bridge.RunAsync(transport, _stop.Token));
        Client = Run(() => McpClient.CreateAsync(
            new StreamClientTransport(_toServer.Writer.AsStream(), _toClient.Reader.AsStream()),
            new McpClientOptions { ClientInfo = new Implementation { Name = "basin-mcp-tests", Version = "1" }, ProtocolVersion = ProtocolVersion },
            cancellationToken: Token));
        _ = Client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification, (_, _) =>
        {
            _ = Interlocked.Increment(ref _listChanged);
            return ValueTask.CompletedTask;
        });
    }

    public const string ProtocolVersion = "2025-11-25";

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public DirectoryInfo Directory { get; }

    public string SocketPath { get; }

    public IpcTestRig? Rig { get; set; }

    public McpBridge Bridge { get; }

    public McpClient Client { get; }

    public int ListChanged => Volatile.Read(ref _listChanged);

    public static McpHarness Create(Func<string, IpcTestRig>? rig, McpBridgeOptions? options = null)
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("basin-mcp-");
        var path = Path.Combine(directory.FullName, "basin-test.sock");
        IpcTestRig? created = null;
        if (rig is not null)
        {
            created = rig(path);
            created.Server.Start();
        }

        return new McpHarness(directory, path, created, options ?? new McpBridgeOptions { PollInterval = TimeSpan.FromMilliseconds(50) });
    }

    public static McpHarness Full(McpBridgeOptions? options = null, Action<Basin.Ipc.IpcServer>? register = null, TestSelectionStore? selection = null) =>
        Create(path => IpcFullRig.Create(path, listen: true, register, selection), options);

    public T Run<T>(Func<Task<T>> call)
    {
        var task = call();
        var deadline = Environment.TickCount64 + 15_000;
        while (!task.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (Rig is { } rig)
            {
                rig.Host.Loop.Dispatch(5);
            }
            else
            {
                Thread.Sleep(5);
            }
        }

        Assert.True(task.IsCompleted, "the call did not complete");
        return task.GetAwaiter().GetResult();
    }

    public T RunValue<T>(Func<ValueTask<T>> call) => Run(() => call().AsTask());

    public void PumpUntil(Func<bool> done, int milliseconds = 5000)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (!done() && Environment.TickCount64 < deadline)
        {
            if (Rig is { } rig)
            {
                rig.Host.Loop.Dispatch(5);
            }
            else
            {
                Thread.Sleep(5);
            }
        }
    }

    public IList<Tool> ListTools() => RunValue(() => Client.ListToolsAsync(new ListToolsRequestParams(), Token)).Tools;

    public CallToolResult Call(string tool, string? argumentsJson = null)
    {
        Dictionary<string, System.Text.Json.JsonElement>? arguments = null;
        if (argumentsJson is not null)
        {
            using var document = System.Text.Json.JsonDocument.Parse(argumentsJson);
            arguments = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        }

        return RunValue(() => Client.CallToolAsync(new CallToolRequestParams { Name = tool, Arguments = arguments }, Token));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Run<int>(async () =>
        {
            await Client.DisposeAsync();
            await _stop.CancelAsync();
            await _toServer.Writer.CompleteAsync();
            await _toClient.Writer.CompleteAsync();
            try
            {
                await _server;
            }
            catch (OperationCanceledException)
            {
            }

            await Bridge.DisposeAsync();
            return 0;
        });
        _stop.Dispose();
        Rig?.Dispose();
        Directory.Delete(recursive: true);
    }
}
