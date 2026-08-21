using Basin.Diagnostics;
using Pixman;
using Wayland;

namespace Basin;

public sealed class FrameCallback
{
    private Action<uint>? _send;
    private WlCallbackResource? _resource;
    private readonly bool _timestamped;

    public FrameCallback(Action<uint> send)
    {
        _send = send;
        BasinCounters.Track();
    }

    public FrameCallback(WlCallbackResource resource, bool timestamped)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _resource = resource;
        _timestamped = timestamped;
        BasinCounters.Track();
    }

    public bool IsSettled => _send is null && _resource is null;

    public void Done(uint timestampMs)
    {
        var send = _send;
        var resource = _resource;
        if (send is null && resource is null)
        {
            return;
        }

        Settle();
        if (resource is not null)
        {
            if (!resource.IsDestroyed)
            {
                resource.SendDone(_timestamped ? timestampMs : 0);
                resource.Destroy();
            }

            return;
        }

        send!(timestampMs);
    }

    public void Cancel() => Settle();

    private void Settle()
    {
        if (_send is not null || _resource is not null)
        {
            _send = null;
            _resource = null;
            BasinCounters.Untrack();
        }
    }
}
