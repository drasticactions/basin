using System.Text.Json;
using System.Text.Json.Nodes;
using Basin.Ipc;
using ModelContextProtocol.Protocol;

namespace BasinMcp;

internal static class McpDiagImageTool
{
    public static bool IsDiagImage(string method) =>
        method is IpcMethodNames.DiagRecord or IpcMethodNames.DiagRecordWindow or IpcMethodNames.DiagCompare;

    public static void WriteParams(Utf8JsonWriter writer, string method, IDictionary<string, JsonElement>? arguments, int defaultMaxDimension)
    {
        arguments ??= new Dictionary<string, JsonElement>();
        var parameters = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
        if (method != IpcMethodNames.DiagCompare && !parameters.ContainsKey("path") && !parameters.ContainsKey("max_dimension") && defaultMaxDimension > 0)
        {
            parameters["max_dimension"] = JsonSerializer.SerializeToElement((long)defaultMaxDimension, McpJson.Info<long>());
        }

        JsonSerializer.Serialize(writer, parameters, McpJson.Info<Dictionary<string, JsonElement>>());
    }

    public static CallToolResult Result(IpcResult result)
    {
        if (JsonNode.Parse(result.Json) is not JsonObject json || json["png"] is not JsonValue encoded
            || !encoded.TryGetValue<string>(out var base64))
        {
            return McpCallMapper.Result(result);
        }

        result.Dispose();
        _ = json.Remove("png");
        var text = System.Text.Encoding.UTF8.GetString(McpSafeIntegers.Rewrite(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())));
        return new CallToolResult
        {
            Content =
            [
                ImageContentBlock.FromBytes(Convert.FromBase64String(base64), "image/png"),
                new TextContentBlock { Text = text },
            ],
            StructuredContent = McpJson.Parse(System.Text.Encoding.UTF8.GetBytes(text)),
        };
    }
}
