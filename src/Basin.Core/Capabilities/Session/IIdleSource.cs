using Wayland.Server;

namespace Basin.Capabilities;

public interface IIdleSource
{
    long IdleMillis { get; }

    bool IsInhibited { get; }

    event Action? Activity;

    event Action? InhibitionChanged;

    IDisposable Inhibit();
}
