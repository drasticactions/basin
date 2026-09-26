using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Basin.Ipc;
using ModelContextProtocol.Protocol;

namespace BasinMcp;

internal static class McpWaitEvent
{
    public const string Name = "wait_event";

    private const int DefaultTimeoutMs = 10_000;
    private const int MaxTimeoutMs = 120_000;
    private const int MaxCollected = 64;

    private const string SchemaTemplate = """
        {"type":"object","properties":{
        "events":{"type":"array","minItems":1,"items":{"type":"string"},"description":"The events to wait for."},
        "match":{"type":"object","description":"Keys that must equal the value at the top level of the event's data. A string that starts and ends with * matches as a substring."},
        "then":{"type":"object","description":"A method to call once the subscription is live, for example {\"method\": \"process/spawn\", \"params\": {\"argv\": [\"foot\"]}}.","properties":{"method":{"type":"string","description":"A method name such as windows/activate, or its tool name."},"params":{"type":"object"}},"required":["method"]},
        "timeout_ms":{"type":"integer","minimum":1,"maximum":120000,"description":"The default is 10000."},
        "collect_ms":{"type":"integer","minimum":0,"description":"Keep collecting matches for this long after the first, up to 64. The default is 0."}},
        "required":["events"]}
        """;

    public static Tool Tool(IReadOnlyList<string> events, bool readOnly)
    {
        var list = events.Count == 0 ? "none, or no compositor is connected" : string.Join(", ", events);
        var description = new StringBuilder()
            .Append("Wait for a compositor event, and optionally run one method after the subscription is live. ")
            .Append("Use then for the action that causes the event, for example process/spawn before window/added, so the event cannot arrive before the wait starts. ")
            .Append("It answers with the first event whose data matches, or with every match for collect_ms after the first. ")
            .Append("Events in this session: ").Append(list).Append('.');
        if (readOnly)
        {
            description.Append(" The bridge is read-only, so then must name a read-only method.");
        }

        var schema = JsonNode.Parse(SchemaTemplate)!.AsObject();
        if (events.Count > 0)
        {
            var names = new JsonArray();
            foreach (var name in events)
            {
                names.Add((JsonNode?)JsonValue.Create(name));
            }

            schema["properties"]!["events"]!["items"]!["enum"] = names;
        }

        return new Tool
        {
            Name = Name,
            Title = "wait for an event",
            Description = description.ToString(),
            InputSchema = McpJson.Parse(schema.ToJsonString()),
            Annotations = new ToolAnnotations { ReadOnlyHint = readOnly, DestructiveHint = !readOnly, IdempotentHint = false, OpenWorldHint = false },
        };
    }

    public static async Task<CallToolResult> RunAsync(
        McpBridge bridge, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        McpWaitEventRequest request;
        try
        {
            request = McpJson.Deserialize<McpWaitEventRequest>(McpJson.Object(arguments))
                ?? throw new JsonException("the arguments are an object");
        }
        catch (JsonException exception)
        {
            return McpJson.Error($"{IpcErrorCodes.InvalidParams}: {exception.Message}");
        }

        var events = request.Events;
        if (events.Count == 0)
        {
            return McpJson.Error($"{IpcErrorCodes.InvalidParams}: name at least one event");
        }

        var match = request.Match is { ValueKind: JsonValueKind.Object } wanted ? wanted : (JsonElement?)null;
        var timeout = request.TimeoutMs ?? DefaultTimeoutMs;
        var collect = request.CollectMs ?? 0;
        if (timeout is < 1 or > MaxTimeoutMs || collect < 0)
        {
            return McpJson.Error($"{IpcErrorCodes.InvalidParams}: 'timeout_ms' is between 1 and {MaxTimeoutMs}, and 'collect_ms' is at least 0");
        }

        McpToolEntry? thenEntry = null;
        JsonElement? thenParams = null;
        if (request.Then is { } then)
        {
            if (!bridge.Table.TryGetMethod(then.Method, out var entry) && !bridge.Table.TryGet(then.Method, out entry))
            {
                return McpJson.Error($"{IpcErrorCodes.UnknownMethod}: 'then' names '{then.Method}', which is not a tool of this bridge");
            }

            thenEntry = entry;
            thenParams = then.Params is { ValueKind: JsonValueKind.Object } given ? given : null;
        }

        var main = bridge.Client!;
        await using var waiter = await BasinIpcClient.ConnectAsync(main.Path, cancellationToken).ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = waiter.SubscribeAsync(events, subscribed.SetResult, deadline.Token).GetAsyncEnumerator(deadline.Token);
        var matches = new List<(string Name, byte[] Data)>();
        byte[]? thenJson = null;
        Task<bool>? pending = null;
        try
        {
            var first = stream.MoveNextAsync().AsTask();
            pending = first;
            var ready = await Task.WhenAny(subscribed.Task, first).ConfigureAwait(false);
            if (ready == first && !subscribed.Task.IsCompleted)
            {
                _ = await first.ConfigureAwait(false);
            }

            if (thenEntry is not null)
            {
                var result = await bridge.InvokeAsync(main, thenEntry, ToDictionary(thenParams), cancellationToken).ConfigureAwait(false);
                if (result.IsError == true)
                {
                    return result;
                }

                thenJson = result.StructuredContent is { } structured
                    ? McpJson.Serialize(structured)
                    : McpJson.Serialize(((TextContentBlock)result.Content[0]).Text);
            }

            var next = first;
            var collecting = false;
            while (true)
            {
                bool more;
                try
                {
                    more = await next.ConfigureAwait(false);
                    pending = null;
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    pending = null;
                    break;
                }

                if (!more)
                {
                    break;
                }

                var item = stream.Current;
                var data = McpSafeIntegers.Rewrite(item.Data);
                if (Matches(data, match))
                {
                    matches.Add((item.Name, data));
                    if (collect == 0 || matches.Count >= MaxCollected)
                    {
                        break;
                    }

                    if (!collecting)
                    {
                        collecting = true;
                        deadline.CancelAfter(TimeSpan.FromMilliseconds(collect));
                    }
                }

                next = stream.MoveNextAsync().AsTask();
                pending = next;
            }
        }
        catch (IpcCallException exception)
        {
            return McpCallMapper.Error(exception);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            if (pending is not null)
            {
                try
                {
                    _ = await pending.ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is OperationCanceledException or IpcCallException || McpBridge.IsConnectionFailure(exception))
                {
                }
            }

            await stream.DisposeAsync().ConfigureAwait(false);
        }

        var caught = new McpCaughtEvent[matches.Count];
        for (var i = 0; i < caught.Length; i++)
        {
            caught[i] = new McpCaughtEvent(matches[i].Name, new IpcRawJson(matches[i].Data));
        }

        var json = McpJson.Serialize(new McpWaitEventResult(caught, thenJson is null ? default : new IpcRawJson(thenJson)));

        if (matches.Count == 0)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"timeout: no matching event in {timeout} ms\n{Encoding.UTF8.GetString(json)}" }],
                IsError = true,
            };
        }

        return McpJson.TextResult(json);
    }

    internal static bool Matches(ReadOnlySpan<byte> data, JsonElement? match)
    {
        if (match is not { } wanted)
        {
            return true;
        }

        var reader = new Utf8JsonReader(data);
        var root = JsonElement.ParseValue(ref reader);
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var key in wanted.EnumerateObject())
        {
            if (!root.TryGetProperty(key.Name, out var value))
            {
                return false;
            }

            if (key.Value.ValueKind == JsonValueKind.String && value.ValueKind == JsonValueKind.String)
            {
                var pattern = key.Value.GetString()!;
                var text = value.GetString()!;
                if (pattern.Length >= 2 && pattern[0] == '*' && pattern[^1] == '*')
                {
                    if (!text.Contains(pattern[1..^1], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                else if (text != pattern)
                {
                    return false;
                }

                continue;
            }

            if (key.Value.ValueKind == JsonValueKind.String && value.ValueKind == JsonValueKind.Number)
            {
                if (key.Value.GetString() != value.GetRawText())
                {
                    return false;
                }

                continue;
            }

            if (!JsonElement.DeepEquals(key.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, JsonElement>? ToDictionary(JsonElement? parameters)
    {
        if (parameters is not { } element)
        {
            return null;
        }

        var dictionary = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            dictionary[property.Name] = property.Value;
        }

        return dictionary;
    }
}
