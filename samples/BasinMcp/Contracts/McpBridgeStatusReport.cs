using System.Text.Json.Serialization;
using Basin.Ipc;

namespace BasinMcp;

internal readonly record struct McpBridgeStatusReport(string? Socket, bool Connected, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IpcVersion? Version, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Tools, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<McpTried>? Tried, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Candidates, McpFilterReport Filters, int MaxDimension);
