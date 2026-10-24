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
                ?? throw new JsonException("The arguments must be an object.");
        }
        catch (JsonException exception)
        {
            return McpJson.Error($"{IpcErrorCodes.InvalidParams}: {exception.Message}");
        }

        var events = request.Events;
        if (events.Count == 0)
        {
            return McpJson.Error($"{IpcErrorCodes.InvalidParams}: Give at least one event.");
        }

        var match = request.Match is { ValueKind: JsonValueKind.Object } wanted ? wanted : (JsonElement?)null;
        var timeout = request.TimeoutMs ?? DefaultTimeoutMs;
        var collect = request.CollectMs ?? 0;
        if (timeout is < 1 or > MaxTimeoutMs || collect < 0)
        {
            return McpJson.Error($"{IpcErrorCodes.InvalidParams}: 'timeout_ms' must be between 1 and {MaxTimeoutMs}, and 'collect_ms' must be at least 0.");
        }

        McpToolEntry? thenEntry = null;
        JsonElement? thenParams = null;
        if (request.Then is { } then)
        {
            if (!bridge.Table.TryGetMethod(then.Method, out var entry) && !bridge.Table.TryGet(then.Method, out entry))
            {
                return McpJson.Error($"{IpcErrorCodes.UnknownMethod}: 'then' names '{then.Method}', but this bridge has no such tool.");
            }

            thenEntry = entry;
            thenParams = then.Params is { ValueKind: JsonValueKind.Object } given ? given : null;
        }

        var main = bridge.Client!;
        CallToolResult? failedThen = null;
        byte[]? thenJson = null;
        Func<CancellationToken, Task<bool>>? run = null;
        if (thenEntry is not null)
        {
            run = async token =>
            {
                var result = await bridge.InvokeAsync(main, thenEntry, ToDictionary(thenParams), token).ConfigureAwait(false);
                if (result.IsError == true)
                {
                    failedThen = result;
                    return false;
                }

                thenJson = result.StructuredContent is { } structured
                    ? McpJson.Serialize(structured)
                    : McpJson.Serialize(((TextContentBlock)result.Content[0]).Text);
                return true;
            };
        }

        IpcEventWaitResult waited;
        try
        {
            waited = await IpcEventWait.RunAsync(
                main.Path,
                events,
                item => IpcEventMatch.Matches(McpSafeIntegers.Rewrite(item.Data), match),
                run,
                TimeSpan.FromMilliseconds(timeout),
                TimeSpan.FromMilliseconds(collect),
                MaxCollected,
                cancellationToken).ConfigureAwait(false);
        }
        catch (IpcCallException exception)
        {
            return McpCallMapper.Error(exception);
        }

        if (failedThen is not null)
        {
            return failedThen;
        }

        var matches = new List<(string Name, byte[] Data)>(waited.Events.Count);
        foreach (var item in waited.Events)
        {
            matches.Add((item.Name, McpSafeIntegers.Rewrite(item.Data)));
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
                Content = [new TextContentBlock { Text = $"timeout: No matching event arrived in {timeout} ms.\n{Encoding.UTF8.GetString(json)}" }],
                IsError = true,
            };
        }

        return McpJson.TextResult(json);
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
