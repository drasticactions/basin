namespace BasinMcp;

internal readonly record struct McpFilterReport(bool ReadOnly, IReadOnlyList<string> Allow, IReadOnlyList<string> Deny);
