using Basin.Config;
using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

public sealed class WmBindings : IDisposable
{
    private readonly RiverWindowManager _wm;
    private readonly RiverXkbBindingsV1? _bindings;
    private readonly Dictionary<WmSeat, SeatState> _seats = [];
    private readonly List<KeyBinding> _keyBindings = [];
    private bool _disposed;

    internal WmBindings(RiverWindowManager wm, RiverXkbBindingsV1? bindings)
    {
        _wm = wm;
        _bindings = bindings;
    }

    public bool IsSupported => _bindings is not null;

    public uint Version => _bindings?.Version ?? 0;

    public event Action<WmSeat>? AteUnboundKey;

    public event Action<WmSeat, Modifiers, Modifiers>? ModifiersChanged;

    public KeyBinding Bind(WmSeat seat, string keysym, Modifiers modifiers, Action? pressed = null) =>
        Bind(seat, Keysym.Require(keysym), modifiers, pressed);

    public KeyBinding Bind(WmSeat seat, uint keysym, Modifiers modifiers, Action? pressed = null)
    {
        ArgumentNullException.ThrowIfNull(seat);
        WmThreadAffinity.Assert();
        var bindings = Require();
        var binding = new KeyBinding(
            _wm,
            bindings.GetXkbBinding(seat.Proxy, keysym, (RiverSeatV1.Modifiers)modifiers));
        if (pressed is not null)
        {
            binding.Pressed += pressed;
        }

        _keyBindings.Add(binding);
        return binding;
    }

    public PointerBinding BindPointer(WmSeat seat, uint button, Modifiers modifiers, Action? pressed = null)
    {
        ArgumentNullException.ThrowIfNull(seat);
        return seat.BindPointer(button, modifiers, pressed);
    }

    public void EnsureNextKeyEaten(WmSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _wm.EnsureManage(nameof(EnsureNextKeyEaten));
        RequireVersion(2, "ensure_next_key_eaten");
        StateFor(seat).Proxy.EnsureNextKeyEaten();
    }

    public void CancelEnsureNextKeyEaten(WmSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _wm.EnsureManage(nameof(CancelEnsureNextKeyEaten));
        RequireVersion(2, "cancel_ensure_next_key_eaten");
        StateFor(seat).Proxy.CancelEnsureNextKeyEaten();
    }

    public void WatchModifiers(WmSeat seat, Modifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _wm.EnsureManage(nameof(WatchModifiers));
        RequireVersion(3, "modifiers_watch");
        StateFor(seat).Proxy.ModifiersWatch((RiverSeatV1.Modifiers)modifiers);
    }

    public WmSubmap EnterSubmap(
        WmSeat seat,
        IReadOnlyList<KeyBinding> bindings,
        TimeSpan timeout,
        Action? exited = null)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(bindings);
        _wm.EnsureManage(nameof(EnterSubmap));
        RequireVersion(2, "ensure_next_key_eaten");

        var submap = new WmSubmap(_wm, this, seat, bindings, timeout);
        if (exited is not null)
        {
            submap.Exited += exited;
        }

        var state = StateFor(seat);
        state.Submap?.Exit();
        state.Submap = submap;
        submap.Enter(_keyBindings);
        return submap;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var binding in _keyBindings)
        {
            binding.DestroyProxy();
        }

        _keyBindings.Clear();
        foreach (var state in _seats.Values)
        {
            state.Submap?.Cancel();
            if (!state.Proxy.IsDestroyed)
            {
                state.Proxy.Destroy();
            }
        }

        _seats.Clear();
        _bindings?.Destroy();
    }

    internal void OnSubmapExited(WmSeat seat, WmSubmap submap)
    {
        if (_seats.TryGetValue(seat, out var state) && ReferenceEquals(state.Submap, submap))
        {
            state.Submap = null;
        }
    }

    private RiverXkbBindingsV1 Require() =>
        _bindings ?? throw new NotSupportedException(
            "the compositor does not offer river_xkb_bindings_v1, so keyboard bindings are unavailable");

    private void RequireVersion(uint since, string request)
    {
        if (Version < since)
        {
            throw new NotSupportedException(
                $"'{request}' requires river_xkb_bindings_v1 version {since}; the compositor bound version {Version}");
        }
    }

    private SeatState StateFor(WmSeat seat)
    {
        if (_seats.TryGetValue(seat, out var existing))
        {
            return existing;
        }

        var bindings = Require();
        RequireVersion(2, "get_seat");
        var proxy = bindings.GetSeat(seat.Proxy);
        var state = new SeatState(proxy);
        _seats[seat] = state;

        proxy.AteUnboundKey += (_, _) =>
        {
            state.Submap?.OnUnboundKey();
            AteUnboundKey?.Invoke(seat);
        };
        proxy.ModifiersUpdate += (_, e) =>
            ModifiersChanged?.Invoke(seat, (Modifiers)e.Old, (Modifiers)e.New);
        return state;
    }

    private sealed class SeatState(RiverXkbBindingsSeatV1 proxy)
    {
        public RiverXkbBindingsSeatV1 Proxy { get; } = proxy;

        public WmSubmap? Submap { get; set; }
    }
}
