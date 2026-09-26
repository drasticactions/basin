using System.Text.Json;
using System.Text.Json.Nodes;
using Basin.Ipc;
using ModelContextProtocol.Protocol;

namespace BasinMcp;

internal sealed class McpToolTable
{
    public static readonly string[] Excluded = [IpcMethodNames.Subscribe, IpcMethodNames.Unsubscribe, IpcMethodNames.Methods];

    private readonly Dictionary<string, McpToolEntry> _byTool = new(StringComparer.Ordinal);

    private McpToolTable(IReadOnlyList<string> events)
    {
        Events = events;
    }

    public static McpToolTable Empty { get; } = new([]);

    public IReadOnlyList<string> Events { get; }

    public IEnumerable<McpToolEntry> Entries => _byTool.Values;

    public IReadOnlyList<string> Methods => [.. _byTool.Values.Select(entry => entry.Method).Order(StringComparer.Ordinal)];

    public static McpToolTable Build(IReadOnlyList<IpcMethodDetail> methods, IReadOnlyList<string> events, McpBridgeOptions options)
    {
        var table = new McpToolTable(events);
        foreach (var method in methods)
        {
            if (Array.IndexOf(Excluded, method.Name) >= 0 || !options.Filter.Allows(method))
            {
                continue;
            }

            var name = McpToolName.FromMethod(method.Name);
            if (McpBridgeTools.IsBridgeTool(name) || name.Length > 64)
            {
                continue;
            }

            var kind = McpCaptureTool.IsCapture(method.Name) ? McpToolKind.Capture : McpToolKind.Plain;
            var schema = SchemaFor(method, kind, options);
            var tool = new Tool
            {
                Name = name,
                Title = method.Name,
                Description = method.Description ?? method.Line ?? method.Name,
                InputSchema = schema,
                Annotations = Annotations(method),
            };
            table._byTool[name] = new McpToolEntry(method.Name, tool, kind, method.ReadOnly);
        }

        return table;
    }

    public bool TryGet(string tool, out McpToolEntry entry) => _byTool.TryGetValue(tool, out entry!);

    public bool TryGetMethod(string method, out McpToolEntry entry) => _byTool.TryGetValue(McpToolName.FromMethod(method), out entry!);

    internal static ToolAnnotations Annotations(IpcMethodDetail method) => new()
    {
        ReadOnlyHint = method.ReadOnly,
        DestructiveHint = method.HasInfo ? method.Destructive : !method.ReadOnly,
        IdempotentHint = method.Idempotent,
        OpenWorldHint = method.Name == IpcMethodNames.ProcessSpawn,
    };

    private static string? DigitStringBranch(JsonObject property)
    {
        if (property["anyOf"] is not JsonArray branches || !branches.Any(branch => branch?["type"]?.GetValue<string>() == "integer"))
        {
            return null;
        }

        foreach (var branch in branches)
        {
            if (branch?["type"]?.GetValue<string>() == "string" && branch["pattern"]?.GetValue<string>() is { } pattern)
            {
                return pattern;
            }
        }

        return null;
    }

    private static JsonElement SchemaFor(IpcMethodDetail method, McpToolKind kind, McpBridgeOptions options)
    {
        if (!method.HasSchema || JsonNode.Parse(method.Schema.ToString()) is not JsonObject schema)
        {
            return McpJson.Parse("""{"type":"object"}""");
        }

        _ = schema.Remove("examples");
        if (schema["properties"] is JsonObject properties)
        {
            var dropped = new List<string>();
            foreach (var (name, value) in properties.ToList())
            {
                if (value is JsonObject property && property["x-basin-fd"] is JsonValue flag && flag.TryGetValue<bool>(out var marked) && marked)
                {
                    dropped.Add(name);
                }
            }

            foreach (var name in dropped)
            {
                _ = properties.Remove(name);
            }

            foreach (var (name, value) in properties.ToList())
            {
                if (value is JsonObject property && DigitStringBranch(property) is { } branch)
                {
                    properties[name] = new JsonObject
                    {
                        ["type"] = "string",
                        ["pattern"] = branch,
                        ["description"] = property["description"]?.GetValue<string>(),
                    };
                }
            }

            if (schema["required"] is JsonArray required)
            {
                for (var i = required.Count - 1; i >= 0; i--)
                {
                    if (required[i] is JsonValue item && item.TryGetValue<string>(out var requiredName) && dropped.Contains(requiredName))
                    {
                        required.RemoveAt(i);
                    }
                }

                if (required.Count == 0)
                {
                    _ = schema.Remove("required");
                }
            }

            if (kind == McpToolKind.Capture)
            {
                McpCaptureTool.Rewrite(properties, options.MaxDimension);
            }
        }

        return McpJson.Parse(schema.ToJsonString());
    }
}
