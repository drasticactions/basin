using Basin.Shell.River.Protocol;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Shell.River;

/// <summary>
/// The river-layer-shell-v1 global. It sends each output's area that is left after exclusive zones, records the default output for unplaced layer surfaces, and carries the layer focus handshake.
/// A window manager that binds it tells the compositor that layer surfaces can map.
/// </summary>
public sealed class RiverLayerShell : IDisposable
{
    private readonly RiverWindowManager _manager;
    private readonly WlGlobal _global;
    private readonly Dictionary<RiverOutput, OutputState> _outputs = [];
    private readonly Dictionary<RiverSeat, SeatState> _seats = [];
    private RiverLayerShellV1Resource? _resource;
    private bool _disposed;

    internal RiverLayerShell(RiverWindowManager manager, WlServerDisplay display)
    {
        _manager = manager;
        _global = display.CreateGlobal(RiverLayerShellV1.Interface, 1, OnBind);
    }

    /// <summary>
    /// Gets a value indicating whether a window manager holds a live binding to this global.
    /// When it is <see langword="false"/>, the compositor must not map layer surfaces.
    /// </summary>
    public bool IsSupported => _resource is { IsDestroyed: false };

    /// <summary>
    /// Gets the output that the window manager chose for layer surfaces that name no output.
    /// It is <see langword="null"/> before the manager chooses one, after that output is removed, and after the manager unbinds.
    /// </summary>
    public IOutput? DefaultOutput { get; private set; }

    /// <summary>
    /// Forgets every output and seat, and removes the global. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _outputs.Clear();
        _seats.Clear();
        _global.Dispose();
    }

    /// <summary>
    /// Sends the area of an output that is left after the exclusive zones of layer surfaces.
    /// After this call the area stops following the output's own box. Nothing is sent when the area did not change.
    /// </summary>
    /// <param name="output">The output that the area belongs to.</param>
    /// <param name="area">The usable area in logical layout coordinates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public void SetNonExclusiveArea(IOutput output, Box area)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (_manager.RiverOutputFor(output) is not { } river || !_outputs.TryGetValue(river, out var state))
        {
            return;
        }

        state.Explicit = true;
        Send(river, state, area);
    }

    internal void RefreshAreas()
    {
        foreach (var (river, state) in _outputs)
        {
            if (!state.Explicit)
            {
                Send(river, state, new Box(river.Position.X, river.Position.Y, river.Width, river.Height));
            }
        }
    }

    private void Send(RiverOutput river, OutputState state, in Box area)
    {
        if (state.AreaSent && state.Area == area)
        {
            return;
        }

        state.Area = area;
        state.AreaSent = true;
        state.Resource?.SendNonExclusiveArea(area.X, area.Y, area.Width, area.Height);
        _manager.MarkManageDirty();
    }

    /// <summary>
    /// Tells whether a layer surface holds exclusive keyboard focus on a seat.
    /// </summary>
    /// <param name="seat">The seat to check.</param>
    /// <returns><see langword="true"/> when the last focus set on the seat is <see cref="LayerFocus.Exclusive"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    public bool HasExclusiveFocus(Basin.Seat.Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        return _manager.RiverSeatFor(seat) is { } river &&
            _seats.TryGetValue(river, out var state) &&
            state.Focus == LayerFocus.Exclusive;
    }

    /// <summary>
    /// Reports the layer focus of a seat without the surface that holds it.
    /// </summary>
    /// <param name="seat">The seat whose focus changed.</param>
    /// <param name="focus">The new layer focus.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    public void SetLayerFocus(Basin.Seat.Seat seat, LayerFocus focus) => SetLayerFocus(seat, focus, null);

    /// <summary>
    /// Reports which layer surface has keyboard focus on a seat. The window manager hears the mode at once.
    /// The surface gets focus at the end of the next manage sequence, by the ordering rule for <paramref name="focus"/>.
    /// </summary>
    /// <param name="seat">The seat whose focus changed.</param>
    /// <param name="focus">The new layer focus.</param>
    /// <param name="surface">The layer surface to focus. It is ignored when <paramref name="focus"/> is <see cref="LayerFocus.None"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    public void SetLayerFocus(Basin.Seat.Seat seat, LayerFocus focus, Surface? surface)
    {
        ArgumentNullException.ThrowIfNull(seat);
        if (_manager.RiverSeatFor(seat) is not { } river || !_seats.TryGetValue(river, out var state))
        {
            return;
        }

        state.FocusSurface = focus == LayerFocus.None ? null : surface;
        if (state.Focus == focus && state.FocusSent)
        {
            return;
        }

        state.Focus = focus;
        state.FocusSent = true;
        switch (focus)
        {
            case LayerFocus.Exclusive:
                state.Resource?.SendFocusExclusive();
                break;
            case LayerFocus.NonExclusive:
                state.Resource?.SendFocusNonExclusive();
                break;
            default:
                state.Resource?.SendFocusNone();
                break;
        }

        _manager.MarkManageDirty();
    }

    internal LayerFocus FocusFor(RiverSeat river, out Surface? surface)
    {
        if (_seats.TryGetValue(river, out var state))
        {
            surface = state.FocusSurface;
            return state.Focus;
        }

        surface = null;
        return LayerFocus.None;
    }

    internal void DropNonExclusiveFocus(RiverSeat river)
    {
        if (!_seats.TryGetValue(river, out var state) || state.Focus != LayerFocus.NonExclusive)
        {
            return;
        }

        state.Focus = LayerFocus.None;
        state.FocusSurface = null;
        state.FocusSent = true;
        state.Resource?.SendFocusNone();
        _manager.MarkManageDirty();
    }

    internal void ResetForNewManager()
    {
        _resource = null;
        _outputs.Clear();
        _seats.Clear();
        DefaultOutput = null;
    }

    internal void OnOutputRemoved(RiverOutput output)
    {
        _outputs.Remove(output);
        if (ReferenceEquals(DefaultOutput, output.Output))
        {
            DefaultOutput = null;
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new RiverLayerShellV1Resource(client, version, id);
        _resource = resource;

        resource.GetOutput += (_, e) =>
        {
            if (_manager.ResolveOutput(e.Output) is not { IsRemoved: false } output)
            {
                return;
            }

            if (_outputs.TryGetValue(output, out var existing) && existing.Resource is { IsDestroyed: false })
            {
                resource.PostError(
                    (uint)RiverLayerShellV1.Error.ObjectAlreadyCreated,
                    "This output already has a river_layer_shell_output_v1.");
                return;
            }

            var outputResource = new RiverLayerShellOutputV1Resource(client, version, e.Id);
            var state = new OutputState { Resource = outputResource };
            _outputs[output] = state;

            Send(output, state, new Box(output.Position.X, output.Position.Y, output.Width, output.Height));

            outputResource.SetDefault += (_, _) =>
            {
                if (_manager.EnsureWindowing())
                {
                    DefaultOutput = output.Output;
                }
            };
            outputResource.DestroyRequest += (_, _) => state.Resource = null;
        };

        resource.GetSeat += (_, e) =>
        {
            if (_manager.ResolveSeat(e.Seat) is not { } seat)
            {
                return;
            }

            if (_seats.TryGetValue(seat, out var existing) && existing.Resource is { IsDestroyed: false })
            {
                resource.PostError(
                    (uint)RiverLayerShellV1.Error.ObjectAlreadyCreated,
                    "This seat already has a river_layer_shell_seat_v1.");
                return;
            }

            var seatResource = new RiverLayerShellSeatV1Resource(client, version, e.Id);
            var state = new SeatState { Resource = seatResource };
            _seats[seat] = state;
            seatResource.DestroyRequest += (_, _) => state.Resource = null;
        };

        resource.DestroyRequest += (_, _) => ResetForNewManager();
        resource.Destroyed += (_, _) => ResetForNewManager();
        _manager.MarkManageDirty();
    }

    private sealed class OutputState
    {
        public RiverLayerShellOutputV1Resource? Resource { get; set; }

        public Box Area { get; set; }

        public bool AreaSent { get; set; }

        public bool Explicit { get; set; }
    }

    private sealed class SeatState
    {
        public RiverLayerShellSeatV1Resource? Resource { get; set; }

        public LayerFocus Focus { get; set; } = LayerFocus.None;

        public Surface? FocusSurface { get; set; }

        public bool FocusSent { get; set; }
    }
}
