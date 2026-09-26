using System.Text.Json;

namespace BasinMcp;

internal sealed record McpThen(string Method, JsonElement? Params = null);
