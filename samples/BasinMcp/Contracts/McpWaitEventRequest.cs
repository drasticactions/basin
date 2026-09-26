using System.Text.Json;

namespace BasinMcp;

internal sealed record McpWaitEventRequest(IReadOnlyList<string> Events, JsonElement? Match = null, McpThen? Then = null, int? TimeoutMs = null, int? CollectMs = null);
