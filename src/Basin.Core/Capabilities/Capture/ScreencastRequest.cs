namespace Basin.Capabilities;

public readonly record struct ScreencastRequest
{
    public required ulong StreamId { get; init; }

    public required CaptureSource Source { get; init; }

    public ScreencastCursorMode Cursor { get; init; }
}
