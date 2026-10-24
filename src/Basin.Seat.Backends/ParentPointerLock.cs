using Basin.Backend.Wayland;

namespace Basin.Seat.Backends;

/// <summary>
/// Shares the pointer lock of a nested compositor's parent between owners. The last live request names the locked output.
/// </summary>
public sealed class ParentPointerLock
{
    private readonly OutputLayout _layout;
    private readonly List<(object Owner, WaylandOutput Output)> _requests = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ParentPointerLock"/> class.
    /// </summary>
    /// <param name="layout">The output layout that converts the release position.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    public ParentPointerLock(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    /// <summary>
    /// Gets the output whose parent pointer is locked now, or <see langword="null"/>.
    /// </summary>
    public WaylandOutput? Output { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a parent output holds the pointer lock now.
    /// </summary>
    public bool IsLocked => Output is not null;

    /// <summary>
    /// Tells whether an owner has a live request.
    /// </summary>
    /// <param name="owner">The owner to find.</param>
    /// <returns><see langword="true"/> when the owner has a request.</returns>
    public bool IsRequested(object owner)
    {
        foreach (var request in _requests)
        {
            if (ReferenceEquals(request.Owner, owner))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Replaces the owner's request with one for this output and applies the newest request. A request that the parent refuses is dropped.
    /// </summary>
    /// <param name="owner">The owner of the request.</param>
    /// <param name="output">The output to lock.</param>
    /// <returns><see langword="true"/> when this output is locked for the owner now.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="output"/> is <see langword="null"/>.</exception>
    public bool Request(object owner, WaylandOutput output)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(output);

        RemoveRequest(owner);
        _requests.Add((owner, output));
        Apply();
        return ReferenceEquals(Output, output) && IsRequested(owner);
    }

    /// <summary>
    /// Removes the owner's request. When no request is left, it unlocks the parent and puts the parent cursor at the given point.
    /// </summary>
    /// <param name="owner">The owner of the request.</param>
    /// <param name="layoutX">The layout x where the parent cursor goes.</param>
    /// <param name="layoutY">The layout y where the parent cursor goes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public void Release(object owner, double layoutX, double layoutY)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!RemoveRequest(owner))
        {
            return;
        }

        if (_requests.Count > 0)
        {
            Apply();
            return;
        }

        if (Output is { } output)
        {
            var box = _layout.BoxOf(output);
            output.SetCursorPositionHint((layoutX - box.X) * output.Scale, (layoutY - box.Y) * output.Scale);
            output.LockPointer(false);
            Output = null;
        }
    }

    /// <summary>
    /// Drops every request for an output that is gone, and applies the next request when the lock was on it.
    /// </summary>
    /// <param name="output">The removed output.</param>
    public void Forget(IOutput output)
    {
        if (ReferenceEquals(Output, output))
        {
            Output = null;
        }

        for (var i = _requests.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_requests[i].Output, output))
            {
                _requests.RemoveAt(i);
            }
        }

        if (Output is null && _requests.Count > 0)
        {
            Apply();
        }
    }

    private void Apply()
    {
        while (_requests.Count > 0)
        {
            var (owner, wanted) = _requests[^1];
            if (ReferenceEquals(Output, wanted))
            {
                return;
            }

            if (Output is { } previous)
            {
                previous.LockPointer(false);
                Output = null;
            }

            if (wanted.LockPointer(true))
            {
                Output = wanted;
                return;
            }

            RemoveRequest(owner);
        }

        if (Output is { } stale)
        {
            stale.LockPointer(false);
            Output = null;
        }
    }

    private bool RemoveRequest(object owner)
    {
        for (var i = 0; i < _requests.Count; i++)
        {
            if (ReferenceEquals(_requests[i].Owner, owner))
            {
                _requests.RemoveAt(i);
                return true;
            }
        }

        return false;
    }
}
