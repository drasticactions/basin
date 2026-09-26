namespace BasinMcp;

internal static class McpBridgeTools
{
    public static bool IsBridgeTool(string name) => name is McpBridgeStatus.Name or McpWaitEvent.Name;
}
