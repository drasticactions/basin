using System.Text.Json;
using System.Text.Json.Serialization;
using Basin.Ipc;

namespace BasinMcp;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(McpBridgeStatusReport))]
[JsonSerializable(typeof(McpWaitEventRequest))]
[JsonSerializable(typeof(McpWaitEventResult))]
[JsonSerializable(typeof(McpCaptureSummary))]
[JsonSerializable(typeof(IpcVersion))]
[JsonSerializable(typeof(IpcCaptureResult))]
[JsonSerializable(typeof(IpcCaptureTo))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(string))]
internal sealed partial class McpJsonContext : JsonSerializerContext
{
}
