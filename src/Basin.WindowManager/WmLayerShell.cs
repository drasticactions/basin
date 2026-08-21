using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

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

    public event Action<WmSeat>? FocusTaken;

    public event Action<WmSeat>? FocusOffered;

    public event Action<WmSeat>? FocusReleased;

    public bool HasExclusiveFocus(WmSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        return _seats.TryGetValue(seat, out var state) && state.Exclusive;
    }

    public void Track(WmSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _ = SeatStateFor(seat);
    }

    public void Track(WmOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _ = OutputStateFor(output);
    }

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
