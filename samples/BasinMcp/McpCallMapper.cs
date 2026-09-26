using System.Text;
using System.Text.Json;
using Basin.Ipc;
using ModelContextProtocol.Protocol;

namespace BasinMcp;

internal static class McpCallMapper
{
    public static void WriteArguments(Utf8JsonWriter writer, IDictionary<string, JsonElement>? arguments) =>
        JsonSerializer.Serialize(writer, McpJson.Object(arguments), McpJson.Info<JsonElement>());

    public static CallToolResult Result(IpcResult result)
    {
        var dropped = result.Fds.Count;
        result.Dispose();
        var json = McpSafeIntegers.Rewrite(result.Json);
        var text = Encoding.UTF8.GetString(json);
        if (dropped > 0)
        {
            text += $"\n{dropped} descriptor{(dropped == 1 ? " was" : "s were")} dropped: the bridge cannot pass descriptors";
        }

        var structured = McpJson.Parse(json);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = structured.ValueKind == JsonValueKind.Object ? structured : null,
        };
    }

    public static CallToolResult Error(IpcCallException exception) => McpJson.Error($"{exception.Code}: {exception.Reason}");

    public static CallToolResult Unavailable(string why) => McpJson.Error($"{IpcErrorCodes.Unavailable}: no compositor: {why}");
}
