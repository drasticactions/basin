using ModelContextProtocol.Protocol;

namespace BasinMcp;

internal sealed record McpToolEntry(string Method, Tool Tool, McpToolKind Kind, bool ReadOnly);
