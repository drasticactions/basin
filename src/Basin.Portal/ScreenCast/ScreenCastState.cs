using Basin.Capabilities;

namespace Basin.Portal;

public sealed class ScreenCastState
{
    private readonly List<ScreenCastStream> _streams = [];

    public uint Types { get; internal set; } = (uint)ScreenCastSourceKind.Monitor;

    public bool Multiple { get; internal set; }

    public ScreencastCursorMode CursorMode { get; internal set; } = ScreencastCursorMode.Hidden;

    public uint PersistMode { get; internal set; }

    public IReadOnlyList<ScreenCastSource>? RestoreCandidates { get; internal set; }

    public bool SourcesSelected { get; internal set; }

    public bool Started { get; internal set; }

    public IReadOnlyList<ScreenCastStream> Streams => _streams;

    internal List<ScreenCastStream> StreamList => _streams;
}
