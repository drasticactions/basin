namespace Basin.Capabilities;

public readonly record struct ScreencastStreamInfo
{
    public required uint NodeId { get; init; }

    public ulong ObjectSerial { get; init; }

    public string? FailureReason { get; init; }
}
