using System.Text.Json;
using Basin.Ipc;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using static BasinMcp.McpLog;

namespace BasinMcp;

internal sealed class McpApprovalRelay : IAsyncDisposable
{
    private const string AnswerField = "answer";

    private static readonly string[] Answers = ["allow_once", "allow_run", "deny"];

    private readonly McpBridge _bridge;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private BasinIpcClient? _client;
    private Task? _loop;
    private McpServer? _session;
    private McpServer? _active;

    public McpApprovalRelay(McpBridge bridge)
    {
        _bridge = bridge;
    }

    public bool IsSubscribed => _client is { IsConnected: true } && _loop is { IsCompleted: false };

    public int Relayed { get; private set; }

    public static bool Supports(McpServer server) => server.ClientCapabilities?.Elicitation is not null;

    public McpServer? Active
    {
        get => Volatile.Read(ref _active);
        set => Volatile.Write(ref _active, value);
    }

    public async Task EnsureAsync(McpServer server, CancellationToken cancellationToken)
    {
        _session ??= server;
        if (IsSubscribed || !Supports(server) || _stop.IsCancellationRequested)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsSubscribed)
            {
                return;
            }

            if (_client is { } dead)
            {
                _client = null;
                await dead.DisposeAsync().ConfigureAwait(false);
            }

            BasinIpcClient client;
            try
            {
                client = await BasinIpcClient.ConnectAsync(_bridge.Options.SocketPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (McpBridge.IsConnectionFailure(exception))
            {
                Log.Debug($"the approval relay could not connect: {exception.Message}");
                return;
            }

            var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stream = client.SubscribeAsync([IpcEventNames.ApprovalRequested], subscribed.SetResult, _stop.Token);
            _client = client;
            _loop = Task.Run(() => RunAsync(client, stream), CancellationToken.None);
            var finished = await Task.WhenAny(subscribed.Task, _loop).ConfigureAwait(false);
            if (finished == subscribed.Task)
            {
                Log.Info($"relaying approval requests to the MCP client as elicitations");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_client is { } client)
        {
            _client = null;
            await client.DisposeAsync().ConfigureAwait(false);
        }

        if (_loop is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException || McpBridge.IsConnectionFailure(exception))
            {
            }
        }

        _stop.Dispose();
        _gate.Dispose();
    }

    private async Task RunAsync(BasinIpcClient client, IAsyncEnumerable<IpcEvent> stream)
    {
        try
        {
            await foreach (var item in stream.ConfigureAwait(false))
            {
                var request = item.Read(IpcJsonContext.Default.IpcApprovalRequested);
                _ = Task.Run(() => RelayAsync(client, request), CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException || McpBridge.IsConnectionFailure(exception))
        {
            Log.Debug($"the approval relay stopped: {exception.Message}");
        }
    }

    private async Task RelayAsync(BasinIpcClient client, IpcApprovalRequested request)
    {
        var answer = "deny";
        try
        {
            var server = Active ?? _session;
            if (server is not null)
            {
                var result = await server.ElicitAsync(Elicitation(request), _stop.Token).ConfigureAwait(false);
                if (result.IsAccepted && result.Content is { } content && content.TryGetValue(AnswerField, out var value)
                    && value.ValueKind == JsonValueKind.String && Array.IndexOf(Answers, value.GetString()) >= 0)
                {
                    answer = value.GetString()!;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Info($"the elicitation for approval {request.Id} failed, so it is denied: {exception.Message}");
        }

        try
        {
            using var reply = await client.CallAsync(
                IpcMethodNames.ApprovalAnswer,
                writer => JsonSerializer.Serialize(writer, new IpcApprovalAnswerParams(request.Id, answer), IpcJsonContext.Default.IpcApprovalAnswerParams),
                _stop.Token).ConfigureAwait(false);
            Relayed++;
        }
        catch (Exception exception) when (exception is IpcCallException || McpBridge.IsConnectionFailure(exception) || exception is OperationCanceledException)
        {
            Log.Debug($"approval {request.Id} was not answered: {exception.Message}");
        }
    }

    private static ElicitRequestParams Elicitation(IpcApprovalRequested request) => new()
    {
        Message = $"The agent asks to call {request.Method}.\n{request.Reason}\n\nArguments: {request.Arguments}",
        RequestedSchema = new ElicitRequestParams.RequestSchema
        {
            Properties =
            {
                [AnswerField] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
                {
                    Title = "Answer",
                    Description = "allow_once runs this call. allow_run also allows later calls to this method for the rest of the run. deny refuses it.",
                    Enum = [.. Answers],
                    Default = "deny",
                },
            },
            Required = [AnswerField],
        },
    };
}
