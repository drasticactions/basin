namespace BasinMcp;

internal static class McpToolName
{
    public static string FromMethod(string method) => method.Replace('/', '_');

    public static string ToMethod(string tool)
    {
        var index = tool.IndexOf('_', StringComparison.Ordinal);
        return index < 0 || tool.IndexOf('_', index + 1) >= 0 ? tool : string.Concat(tool.AsSpan(0, index), "/", tool.AsSpan(index + 1));
    }
}
