namespace Basin.Capabilities;

public readonly record struct FrameState
{
    public string? Title { get; init; }

    public string? AppId { get; init; }

    public FrameIcon Icon { get; init; }

    public bool Active { get; init; }

    public bool Maximized { get; init; }

    public bool Fullscreen { get; init; }

    public bool Resizing { get; init; }

    public FrameCapabilities Capabilities { get; init; }
}
