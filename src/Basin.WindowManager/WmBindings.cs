using Basin.Config;
using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// The key bindings through <c>river_xkb_bindings_v1</c>, and pointer bindings through the seat. It keeps every key binding it creates and destroys them on dispose.
/// </summary>
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

    /// <summary>
    /// Gets a value indicating whether the compositor serves <c>river_xkb_bindings_v1</c>. Key binding members throw <see cref="NotSupportedException"/> when it does not.
    /// </summary>
    public bool IsSupported => _bindings is not null;

    /// <summary>
    /// Gets the bound version of <c>river_xkb_bindings_v1</c>, or zero when it is not bound.
    /// </summary>
    public uint Version => _bindings?.Version ?? 0;

    /// <summary>
    /// Occurs when the compositor withheld an unbound key after <see cref="EnsureNextKeyEaten"/>. An open submap on the seat exits in the next manage sequence.
    /// </summary>
    public event Action<WmSeat>? AteUnboundKey;

    /// <summary>
    /// Occurs when a watched modifier changes on a seat. The arguments are the seat, the old modifiers and the new modifiers.
    /// </summary>
    public event Action<WmSeat, Modifiers, Modifiers>? ModifiersChanged;

    /// <summary>
    /// Creates a key binding from a keysym name. It stays inert until <see cref="KeyBinding.Enable"/> runs in a manage sequence.
    /// </summary>
    /// <param name="seat">The seat whose keyboard the binding watches.</param>
    /// <param name="keysym">The keysym name, for example <c>Return</c>.</param>
    /// <param name="modifiers">The modifiers that must be held.</param>
    /// <param name="pressed">An optional handler for <see cref="KeyBinding.Pressed"/>.</param>
    /// <returns>The new binding.</returns>
    /// <exception cref="ArgumentException">No keysym has the name.</exception>
    /// <exception cref="NotSupportedException">The compositor does not serve <c>river_xkb_bindings_v1</c>.</exception>
    public KeyBinding Bind(WmSeat seat, string keysym, Modifiers modifiers, Action? pressed = null) =>
        Bind(seat, Keysym.Require(keysym), modifiers, pressed);

    /// <summary>
    /// Creates a key binding from a numeric keysym, with no native lookup. It stays inert until <see cref="KeyBinding.Enable"/> runs in a manage sequence.
    /// </summary>
    /// <param name="seat">The seat whose keyboard the binding watches.</param>
    /// <param name="keysym">The xkbcommon keysym value.</param>
    /// <param name="modifiers">The modifiers that must be held.</param>
    /// <param name="pressed">An optional handler for <see cref="KeyBinding.Pressed"/>.</param>
    /// <returns>The new binding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The compositor does not serve <c>river_xkb_bindings_v1</c>.</exception>
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

    /// <summary>
    /// Creates a pointer binding on a seat, as <see cref="WmSeat.BindPointer"/> does. This object does not keep it.
    /// </summary>
    /// <param name="seat">The seat whose pointer the binding watches.</param>
    /// <param name="button">A Linux input event code, for example <c>BTN_LEFT</c>.</param>
    /// <param name="modifiers">The modifiers that must be held.</param>
    /// <param name="pressed">An optional handler for <see cref="PointerBinding.Pressed"/>.</param>
    /// <returns>The new binding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    public PointerBinding BindPointer(WmSeat seat, uint button, Modifiers modifiers, Action? pressed = null)
    {
        ArgumentNullException.ThrowIfNull(seat);
        return seat.BindPointer(button, modifiers, pressed);
    }

    /// <summary>
    /// Withholds the next non-modifier key press on a seat from the focused surface. A bound key fires its binding, and an unbound key raises <see cref="AteUnboundKey"/>.
    /// </summary>
    /// <param name="seat">The seat to act on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">Key bindings are not served or the bound version is lower than 2.</exception>
    public void EnsureNextKeyEaten(WmSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _wm.EnsureManage(nameof(EnsureNextKeyEaten));
        RequireVersion(2, "ensure_next_key_eaten");
        StateFor(seat).Proxy.EnsureNextKeyEaten();
    }

    /// <summary>
    /// Cancels <see cref="EnsureNextKeyEaten"/> on a seat if no key was withheld yet. A key can still be withheld before the cancel arrives.
    /// </summary>
    /// <param name="seat">The seat to act on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">Key bindings are not served or the bound version is lower than 2.</exception>
    public void CancelEnsureNextKeyEaten(WmSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _wm.EnsureManage(nameof(CancelEnsureNextKeyEaten));
        RequireVersion(2, "cancel_ensure_next_key_eaten");
        StateFor(seat).Proxy.CancelEnsureNextKeyEaten();
    }

    /// <summary>
    /// Asks the compositor to report changes to a set of modifiers on a seat through <see cref="ModifiersChanged"/>.
    /// </summary>
    /// <param name="seat">The seat to watch.</param>
    /// <param name="modifiers">The modifiers whose changes are reported.</param>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">Key bindings are not served or the bound version is lower than 3.</exception>
    public void WatchModifiers(WmSeat seat, Modifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(seat);
        _wm.EnsureManage(nameof(WatchModifiers));
        RequireVersion(3, "modifiers_watch");
        StateFor(seat).Proxy.ModifiersWatch((RiverSeatV1.Modifiers)modifiers);
    }

    /// <summary>
    /// Enters a submap on a seat. It enables the given bindings, disables every other enabled key binding, withholds the next key, and exits any submap open on the seat.
    /// </summary>
    /// <param name="seat">The seat to act on.</param>
    /// <param name="bindings">The submap's bindings. Each must come from this object.</param>
    /// <param name="timeout">The time after which the submap exits in a manage sequence. Zero or less means no timeout.</param>
    /// <param name="exited">An optional handler for <see cref="WmSubmap.Exited"/>.</param>
    /// <returns>The open submap.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="seat"/> or <paramref name="bindings"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No manage sequence is in progress.</exception>
    /// <exception cref="NotSupportedException">Key bindings are not served or the bound version is lower than 2.</exception>
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

    /// <summary>
    /// Destroys every key binding, cancels open submaps and destroys the bindings global. A second call does nothing.
    /// </summary>
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
