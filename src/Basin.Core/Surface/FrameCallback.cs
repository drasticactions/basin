using Basin.Diagnostics;
using Pixman;
using Wayland;

namespace Basin;

/// <summary>
/// One pending callback that settles once, either through <see cref="Done"/> or <see cref="Cancel"/>. A call after it settles does nothing.
/// </summary>
public sealed class FrameCallback
{
    private Action<uint>? _send;
    private WlCallbackResource? _resource;
    private readonly bool _timestamped;

    /// <summary>Initializes a new instance of the <see cref="FrameCallback"/> class that calls a delegate.</summary>
    /// <param name="send">Called once by <see cref="Done"/> with the timestamp in milliseconds.</param>
    public FrameCallback(Action<uint> send)
    {
        _send = send;
        BasinCounters.Track();
    }

    /// <summary>Initializes a new instance of the <see cref="FrameCallback"/> class that sends <c>wl_callback.done</c> and destroys the resource.</summary>
    /// <param name="resource">The callback resource.</param>
    /// <param name="timestamped">A value indicating whether <c>done</c> carries the timestamp. When it is <see langword="false"/>, <c>done</c> carries zero.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    public FrameCallback(WlCallbackResource resource, bool timestamped)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _resource = resource;
        _timestamped = timestamped;
        BasinCounters.Track();
    }

    /// <summary>Gets a value indicating whether the callback is done or cancelled.</summary>
    public bool IsSettled => _send is null && _resource is null;

    /// <summary>
    /// Settles the callback and delivers it. A resource that the client already destroyed gets no event.
    /// </summary>
    /// <param name="timestampMs">The timestamp in milliseconds.</param>
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

    /// <summary>Settles the callback without delivery.</summary>
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
