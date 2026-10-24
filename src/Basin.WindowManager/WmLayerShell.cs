using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// The river layer shell binding. Its presence tells the compositor that layer surfaces can map. It reports layer-surface keyboard focus per seat and usable areas per output.
/// </summary>
public sealed class WmLayerShell : IDisposable
{
    private readonly RiverWindowManager _wm;
    private readonly RiverLayerShellV1 _proxy;
    private readonly Dictionary<WmOutput, RiverLayerShellOutputV1> _outputs = [];
    private readonly Dictionary<WmSeat, SeatState> _seats = [];
    private bool _disposed;

    internal WmLayerShell(RiverWindowManager wm, RiverLayerShellV1 proxy)
    {
        _wm = wm;
        _proxy = proxy;
    }

    /// <summary>
    /// Occurs when the event arrives that a layer surface takes exclusive keyboard focus on a seat. The compositor ignores focus requests on that seat until focus is released or offered.
    /// </summary>
    public event Action<WmSeat>? FocusTaken;

    /// <summary>
    /// Occurs when the event arrives that a layer surface takes non-exclusive keyboard focus on a seat. The manager keeps control of focus.
    /// </summary>
    public event Action<WmSeat>? FocusOffered;

    /// <summary>
    /// Occurs when the event arrives that no layer surface has keyboard focus on a seat, so the manager can give focus back.
    /// </summary>
    public event Action<WmSeat>? FocusReleased;

    /// <summary>
    /// Tests whether a layer surface holds exclusive keyboard focus on a seat.
    /// </summary>
    /// <param name="seat">The seat to test.</param>
    /// <returns><see langword="true"/> after <see cref="FocusTaken"/> and before the next release or offer. It is <see langword="false"/> for a seat that is not tracked.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    public bool HasExclusiveFocus(WmSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        return _seats.TryGetValue(seat, out var state) && state.Exclusive;
    }

    /// <summary>
    /// Binds the seat's layer-shell state, so its focus events arrive. A second call for the same seat does nothing.
    /// </summary>
    /// <param name="seat">The seat to track.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    public void Track(WmSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _ = SeatStateFor(seat);
    }

    /// <summary>
    /// Binds the output's layer-shell state, so <see cref="WmOutput.NonExclusiveArea"/> updates. A second call for the same output does nothing.
    /// </summary>
    /// <param name="output">The output to track.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public void Track(WmOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _ = OutputStateFor(output);
    }

    /// <summary>
    /// Destroys every per-seat and per-output object and the layer shell binding. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var output in _outputs.Values)
        {
            if (!output.IsDestroyed)
            {
                output.Destroy();
            }
        }

        foreach (var seat in _seats.Values)
        {
            if (!seat.Proxy.IsDestroyed)
            {
                seat.Proxy.Destroy();
            }
        }

        _outputs.Clear();
        _seats.Clear();
        _proxy.Destroy();
    }

    internal RiverLayerShellOutputV1 OutputStateFor(WmOutput output)
    {
        if (_outputs.TryGetValue(output, out var existing))
        {
            return existing;
        }

        var proxy = _proxy.GetOutput(output.Proxy);
        _outputs[output] = proxy;
        proxy.NonExclusiveArea += (_, e) =>
            output.ReportNonExclusiveArea(new Rect(e.X, e.Y, e.Width, e.Height));
        output.Removed += () =>
        {
            if (_outputs.Remove(output, out var removed) && !removed.IsDestroyed)
            {
                removed.Destroy();
            }
        };
        return proxy;
    }

    private SeatState SeatStateFor(WmSeat seat)
    {
        if (_seats.TryGetValue(seat, out var existing))
        {
            return existing;
        }

        var proxy = _proxy.GetSeat(seat.Proxy);
        var state = new SeatState(proxy);
        _seats[seat] = state;

        proxy.FocusExclusive += (_, _) =>
        {
            state.Exclusive = true;
            FocusTaken?.Invoke(seat);
        };
        proxy.FocusNonExclusive += (_, _) =>
        {
            state.Exclusive = false;
            FocusOffered?.Invoke(seat);
        };
        proxy.FocusNone += (_, _) =>
        {
            state.Exclusive = false;
            FocusReleased?.Invoke(seat);
        };
        seat.Removed += () =>
        {
            if (_seats.Remove(seat, out var removed) && !removed.Proxy.IsDestroyed)
            {
                removed.Proxy.Destroy();
            }
        };
        return state;
    }

    private sealed class SeatState(RiverLayerShellSeatV1 proxy)
    {
        public RiverLayerShellSeatV1 Proxy { get; } = proxy;

        public bool Exclusive { get; set; }
    }
}
