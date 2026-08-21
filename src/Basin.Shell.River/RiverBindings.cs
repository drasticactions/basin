using Basin.Shell.River.Protocol;
using Wayland.Server;
using Xkb;

namespace Basin.Shell.River;

internal sealed class RiverBindings : IDisposable
{
    private readonly RiverWindowManager _manager;
    private readonly WlGlobal _global;
    private readonly List<RiverKeyBinding> _bindings = [];
    private readonly Dictionary<RiverSeat, BindingSeat> _seats = [];
    private RiverXkbBindingsV1Resource? _resource;
    private XkbState? _overrideState;
    private XkbKeymap? _overrideSource;
    private bool _disposed;

    internal RiverBindings(RiverWindowManager manager, WlServerDisplay display)
    {
        _manager = manager;
        _global = display.CreateGlobal(RiverXkbBindingsV1.Interface, RiverXkbBindingsV1.Interface.Version, OnBind);
    }

    internal bool IsBound => _resource is { IsDestroyed: false };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _overrideState?.Dispose();
        _overrideState = null;
        _bindings.Clear();
        _seats.Clear();
        _global.Dispose();
    }

    internal bool HandleKey(RiverSeat seat, uint key, bool pressed)
    {
        if (!IsBound)
        {
            return false;
        }

        var keyboard = seat.Seat.Keyboard;
        var state = keyboard.State;
        if (state is null)
        {
            return false;
        }

        var modifiers = CurrentModifiers(keyboard);
        var seatState = SeatStateFor(seat);

        if (!pressed)
        {
            if (seatState.Held.Remove(key, out var held))
            {
                held.SendReleased();
                _manager.MarkManageDirty();
                return true;
            }

            return seatState.EatenKeys.Remove(key);
        }

        var match = Match(seat, state, key, modifiers);
        if (match is not null)
        {
            StopRepeats(seatState);

            seatState.Held[key] = match;
            seatState.EatenPending = false;
            match.SendPressed();
            _manager.MarkManageDirty();
            return true;
        }

        var stopped = StopRepeats(seatState);

        if (seatState.EatenPending && !IsModifierKey(state, key))
        {
            seatState.EatenPending = false;
            seatState.EatenKeys.Add(key);
            if (seatState.Resource is { IsDestroyed: false, Version: >= 2 } seatResource)
            {
                seatResource.SendAteUnboundKey();
            }

            _manager.MarkManageDirty();
            return true;
        }

        if (stopped)
        {
            _manager.MarkManageDirty();
        }

        return false;
    }

    private static bool StopRepeats(BindingSeat seatState)
    {
        var sent = false;
        foreach (var (_, held) in seatState.Held)
        {
            sent |= held.SendStopRepeat();
        }

        return sent;
    }

    internal void HandleModifiers(RiverSeat seat)
    {
        if (!IsBound || !_seats.TryGetValue(seat, out var seatState) || seatState.Watched == 0)
        {
            return;
        }

        var current = CurrentModifiers(seat.Seat.Keyboard);
        if (current == seatState.LastModifiers)
        {
            return;
        }

        var changed = current ^ seatState.LastModifiers;
        var previous = seatState.LastModifiers;
        seatState.LastModifiers = current;
        if ((changed & seatState.Watched) == 0)
        {
            return;
        }

        if (seatState.Resource is not { IsDestroyed: false, Version: >= 3 } seatResource)
        {
            return;
        }

        seatResource.SendModifiersUpdate(previous, current);
        _manager.MarkManageDirty();
    }

    internal void ResetForNewManager()
    {
        foreach (var binding in _bindings)
        {
            binding.MakeInert();
        }

        _bindings.Clear();
        _seats.Clear();
        _resource = null;
    }

    private RiverKeyBinding? Match(RiverSeat seat, XkbState state, uint key, RiverSeatV1.Modifiers modifiers)
    {
        var keycode = key + 8;
        var active = state.GetKeyOneSym(keycode).Value;

        RiverKeyBinding? matched = null;
        foreach (var binding in _bindings)
        {
            if (!binding.IsEnabled || !ReferenceEquals(binding.Seat, seat) || binding.Modifiers != modifiers)
            {
                continue;
            }

            var keysym = binding.LayoutOverride is { } layout
                ? TranslateUnder(state, keycode, layout)
                : active;
            if (keysym == binding.Keysym)
            {
                matched ??= binding;
            }
        }

        return matched;
    }

    private uint TranslateUnder(XkbState active, uint keycode, uint layout)
    {
        var keymap = active.Keymap;
        if (layout >= keymap.LayoutCount)
        {
            return 0;
        }

        if (_overrideState is null || !ReferenceEquals(_overrideSource, keymap))
        {
            _overrideState?.Dispose();
            _overrideState = keymap.CreateState();
            _overrideSource = keymap;
        }

        _overrideState.UpdateMask(0, 0, 0, 0, 0, layout);
        return _overrideState.GetKeyOneSym(keycode).Value;
    }

    private static bool IsModifierKey(XkbState state, uint key)
    {
        var syms = state.GetKeySyms(key + 8);
        foreach (var sym in syms)
        {
            if (sym.Value is >= 0xffe1 and <= 0xffee)
            {
                return true;
            }
        }

        return syms.Length == 0;
    }

    private static RiverSeatV1.Modifiers CurrentModifiers(Basin.Seat.SeatKeyboard keyboard)
    {
        var state = keyboard.State;
        if (state is null)
        {
            return RiverSeatV1.Modifiers.None;
        }

        var result = RiverSeatV1.Modifiers.None;
        if (state.IsModActive("Shift"))
        {
            result |= RiverSeatV1.Modifiers.Shift;
        }

        if (state.IsModActive("Control"))
        {
            result |= RiverSeatV1.Modifiers.Ctrl;
        }

        if (state.IsModActive("Mod1"))
        {
            result |= RiverSeatV1.Modifiers.Mod1;
        }

        if (state.IsModActive("Mod3"))
        {
            result |= RiverSeatV1.Modifiers.Mod3;
        }

        if (state.IsModActive("Mod4"))
        {
            result |= RiverSeatV1.Modifiers.Mod4;
        }

        if (state.IsModActive("Mod5"))
        {
            result |= RiverSeatV1.Modifiers.Mod5;
        }

        return result;
    }

    private BindingSeat SeatStateFor(RiverSeat seat)
    {
        if (!_seats.TryGetValue(seat, out var state))
        {
            state = new BindingSeat();
            _seats[seat] = state;
        }

        return state;
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new RiverXkbBindingsV1Resource(client, version, id);
        _resource = resource;

        resource.GetXkbBinding += (_, e) =>
        {
            if (_manager.ResolveSeat(e.Seat) is not { } seat)
            {
                return;
            }

            var bindingResource = new RiverXkbBindingV1Resource(client, version, e.Id);
            var binding = new RiverKeyBinding(_manager, seat, bindingResource, e.Keysym, e.Modifiers);
            _bindings.Add(binding);
            bindingResource.DestroyRequest += (_, _) => _bindings.Remove(binding);
        };

        resource.GetSeat += (_, e) =>
        {
            if (_manager.ResolveSeat(e.Seat) is not { } seat)
            {
                return;
            }

            var seatState = SeatStateFor(seat);
            if (seatState.Resource is { IsDestroyed: false })
            {
                resource.PostError(
                    (uint)RiverXkbBindingsV1.Error.ObjectAlreadyCreated,
                    "this seat already has a river_xkb_bindings_seat_v1");
                return;
            }

            var seatResource = new RiverXkbBindingsSeatV1Resource(client, version, e.Id);
            seatState.Resource = seatResource;
            seatState.LastModifiers = CurrentModifiers(seat.Seat.Keyboard);

            seatResource.EnsureNextKeyEaten += (_, _) =>
            {
                if (_manager.EnsureWindowing())
                {
                    seatState.EatenPending = true;
                }
            };
            seatResource.CancelEnsureNextKeyEaten += (_, _) =>
            {
                if (_manager.EnsureWindowing())
                {
                    seatState.EatenPending = false;
                }
            };
            seatResource.ModifiersWatch += (_, e2) =>
            {
                if (_manager.EnsureWindowing())
                {
                    seatState.Watched = e2.Modifiers;
                    seatState.LastModifiers = CurrentModifiers(seat.Seat.Keyboard);
                }
            };
            seatResource.DestroyRequest += (_, _) => seatState.Resource = null;
        };

        resource.DestroyRequest += (_, _) => _resource = null;
        resource.Destroyed += (_, _) => _resource = null;
    }

    private sealed class BindingSeat
    {
        public RiverXkbBindingsSeatV1Resource? Resource { get; set; }

        public Dictionary<uint, RiverKeyBinding> Held { get; } = [];

        public HashSet<uint> EatenKeys { get; } = [];

        public bool EatenPending { get; set; }

        public RiverSeatV1.Modifiers Watched { get; set; }

        public RiverSeatV1.Modifiers LastModifiers { get; set; }
    }
}
