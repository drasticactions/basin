using Basin.Capabilities;
using Basin.Plasma.Protocol;

namespace Basin.Plasma;

public sealed class ScreencastStream
{
    internal ScreencastStream(ZkdeScreencastStreamUnstableV1Resource resource, ulong id, ScreencastCursorMode cursor)
    {
        Resource = resource;
        Id = id;
        Cursor = cursor;
    }

    public ulong Id { get; }

    internal ZkdeScreencastStreamUnstableV1Resource Resource { get; }

    internal ScreencastCursorMode Cursor { get; }

    internal CaptureSource Source { get; set; }

    internal StreamState State { get; set; }

    internal IOutput? VirtualOutput { get; set; }

    internal IOutput? WatchedOutput { get; set; }

    internal Action? OutputDestroyedHandler { get; set; }

    internal enum StreamState
    {
        Pending,
        Live,
        Failed,
        Closed,
    }
}
