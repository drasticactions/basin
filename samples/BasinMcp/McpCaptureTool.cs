using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Basin.Ipc;
using ModelContextProtocol.Protocol;

namespace BasinMcp;

internal static class McpCaptureTool
{
    public static bool IsCapture(string method) =>
        method is IpcMethodNames.CaptureOutput or IpcMethodNames.CaptureWindow or IpcMethodNames.CaptureRegion;

    public static void Rewrite(JsonObject properties, int maxDimension)
    {
        _ = properties.Remove("to");
        properties["path"] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "An absolute path. The compositor writes the full-size PNG there and the result is text. Without a path, the image comes back inline.",
        };
        properties["max_dimension"] = new JsonObject
        {
            ["type"] = "integer",
            ["minimum"] = 0,
            ["description"] = maxDimension > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Scale the image down until its longer side is at most this many pixels. Inline, the default is {maxDimension}. With a path, the default is full size. 0 means full size.")
                : "Scale the image down until its longer side is at most this many pixels. 0 means full size, which is the default.",
        };
    }

    public static void WriteParams(Utf8JsonWriter writer, IDictionary<string, JsonElement>? arguments, int defaultMaxDimension)
    {
        arguments ??= new Dictionary<string, JsonElement>();
        var path = arguments.TryGetValue("path", out var given) && given.ValueKind == JsonValueKind.String ? given.GetString() : null;
        long? max = arguments.TryGetValue("max_dimension", out var asked) && asked.ValueKind == JsonValueKind.Number && asked.TryGetInt64(out var value)
            ? value
            : null;
        max ??= path is null && !arguments.ContainsKey("scale") ? defaultMaxDimension : null;

        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, element) in arguments)
        {
            if (name is not ("path" or "max_dimension" or "to"))
            {
                parameters[name] = element;
            }
        }

        if (max is > 0)
        {
            parameters["max_dimension"] = JsonSerializer.SerializeToElement(max.Value, McpJson.Info<long>());
        }

        var target = path is null ? IpcCaptureTo.Inline : IpcCaptureTo.ToPath(path);
        parameters["to"] = JsonSerializer.SerializeToElement(target, McpJson.Info<IpcCaptureTo>());
        JsonSerializer.Serialize(writer, parameters, McpJson.Info<Dictionary<string, JsonElement>>());
    }

    public static CallToolResult Result(IpcResult result)
    {
        var reply = result.Read(IpcJsonContext.Default.IpcCaptureResult);
        if (reply.Png is not { } png)
        {
            return McpCallMapper.Result(result);
        }

        result.Dispose();
        var summary = McpJson.Serialize(new McpCaptureSummary(reply.Width, reply.Height, "image/png"));
        return new CallToolResult
        {
            Content =
            [
                ImageContentBlock.FromBytes(png, "image/png"),
                new TextContentBlock { Text = System.Text.Encoding.UTF8.GetString(summary) },
            ],
            StructuredContent = McpJson.Parse(summary),
        };
    }
}
