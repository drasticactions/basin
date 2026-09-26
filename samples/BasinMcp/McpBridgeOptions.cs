namespace BasinMcp;

internal sealed record McpBridgeOptions
{
    public const int DefaultMaxDimension = 1024;

    public string? SocketPath { get; init; }

    public McpMethodFilter Filter { get; init; } = McpMethodFilter.None;

    public int MaxDimension { get; init; } = DefaultMaxDimension;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public bool Reconnect { get; init; } = true;
}
