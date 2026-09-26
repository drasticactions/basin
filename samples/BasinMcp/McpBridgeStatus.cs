using Basin.Ipc;
using ModelContextProtocol.Protocol;

namespace BasinMcp;

internal static class McpBridgeStatus
{
    public const string Name = "bridge_status";

    public static Tool Tool { get; } = new()
    {
        Name = Name,
        Title = "bridge status",
        Description = "Report the control socket this bridge uses, whether it is connected, the compositor's version, the filters, and why the last connection attempt failed.",
        InputSchema = McpJson.Parse("""{"type":"object","properties":{}}"""),
        Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false },
    };

    public static CallToolResult Result(McpBridge bridge)
    {
        var client = bridge.Client;
        var connected = client is { IsConnected: true };
        var filter = bridge.Options.Filter;
        var report = new McpBridgeStatusReport(
            connected ? client!.Path : bridge.LastAttempt,
            connected,
            connected ? bridge.Version : null,
            connected ? bridge.Table.Methods.Count : null,
            connected ? null : [new McpTried(bridge.LastAttempt ?? "(none resolved)", bridge.LastFailure)],
            connected ? null : BasinIpcClient.Candidates(),
            new McpFilterReport(filter.ReadOnly, [.. filter.Allow.Select(glob => glob.Text)], [.. filter.Deny.Select(glob => glob.Text)]),
            bridge.Options.MaxDimension);
        return McpJson.TextResult(McpJson.Serialize(report));
    }
}
