using Wayland;

namespace Basin.Seat;

public sealed class SeatDataDevice : Capabilities.IDragTracker
{
    public const string IconRole = "dnd_icon";

    private readonly Seat _seat;
    private DragGrab? _drag;
    private Surface? _dragIcon;

    internal SeatDataDevice(Seat seat)
    {
        _seat = seat;
    }

    public Capabilities.ISelectionStore? Store { get; set; }

    public DataSource? Selection { get; private set; }

    public DataSource? PrimarySelection { get; private set; }

    public event Action<DataSource?>? SelectionChanged;

    public event Action<DataSource?>? PrimarySelectionChanged;

    public event Action<DataSource?>? SelectionRequested;

    public event Action<DragEvent>? DragStarted;

    public event Action? DragEnded;

    public DataSource? DraggingSource => _drag?.Source;

    public Surface? DraggingIcon => _drag is null ? null : _dragIcon;

    public event Action? DragChanged;

    public bool StartDrag(DataSource source, Surface? icon = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (icon is not null && !icon.TrySetRole(IconRole, this) && icon.RoleObject != this)
        {
            return false;
        }

        _drag?.End(Capabilities.DragOutcome.Cancelled);
        _drag = new DragGrab(_seat, this, source);
        _dragIcon = icon;
        WatchDragSource(source);
        _seat.Pointer.StartGrab(_drag);
        DragChanged?.Invoke();
        DragStarted?.Invoke(new DragEvent(source, null, icon));

        _drag.Enter(_seat.Pointer.Focus, _seat.Pointer.X, _seat.Pointer.Y);
        return true;
    }

    private void WatchDragSource(DataSource? source)
    {
        if (source is null)
        {
            return;
        }

        var grab = _drag;
        source.Destroyed += () =>
        {
            if (_drag == grab)
            {
                EndDrag(Capabilities.DragOutcome.Cancelled);
            }
        };
    }

    public void EndDrag(Capabilities.DragOutcome outcome) => _drag?.End(outcome);

    public void SetSelection(DataSource? source)
    {
        if (Selection == source)
        {
            return;
        }

        var old = Selection;
        Selection = source;
        if (source is not null)
        {
            source.Destroyed += () =>
            {
                if (Selection == source)
                {
                    Selection = null;
                    OfferSelectionTo(_seat.Keyboard.Focus);
                }
            };
        }

        old?.Cancel();
        OfferSelectionTo(_seat.Keyboard.Focus);
        SelectionChanged?.Invoke(source);
    }

    public void SetPrimarySelection(DataSource? source)
    {
        if (PrimarySelection == source)
        {
            return;
        }

        var old = PrimarySelection;
        PrimarySelection = source;
        if (source is not null)
        {
            source.Destroyed += () =>
            {
                if (PrimarySelection == source)
                {
                    PrimarySelection = null;
                    PrimarySelectionChanged?.Invoke(null);
                }
            };
        }

        old?.Cancel();
        PrimarySelectionChanged?.Invoke(source);
    }

    internal void OnKeyboardFocus(Surface surface) => OfferSelectionTo(surface);

    internal void WireDevice(SeatClient seatClient, WlDataDeviceResource device)
    {
        device.SetSelection += (_, e) =>
        {
            var source = DataSourceRegistry.Resolve(e.Source);
            if (SelectionRequested is { } handler)
            {
                handler(source);
            }
            else if (Store is { } store)
            {
                store.SetSelection(Capabilities.SelectionKind.Clipboard, source, e.Serial);
            }
            else
            {
                SetSelection(source);
            }
        };

        device.StartDrag += (_, e) => HandleStartDrag(seatClient, e);

        if (_seat.Keyboard.Focus is { } focus && _seat.ClientOf(focus) == seatClient)
        {
            OfferSelectionTo(focus);
        }
    }

    private void HandleStartDrag(SeatClient seatClient, WlDataDeviceResource.StartDragEventArgs e)
    {
        var origin = _seat.ResolveSurface(e.Origin);
        if (origin is null || !_seat.ValidateImplicitGrabSerial(e.Serial) || _seat.ClientOf(origin) != seatClient)
        {
            return;
        }

        Surface? icon = null;
        if (e.Icon is { } iconResource)
        {
            icon = _seat.ResolveSurface(iconResource);
            if (icon is not null && !icon.TrySetRole(IconRole, this) && icon.RoleObject != this)
            {
                return;
            }
        }

        var source = DataSourceRegistry.Resolve(e.Source);
        _drag?.End(Capabilities.DragOutcome.Cancelled);
        _drag = new DragGrab(_seat, this, source);
        _dragIcon = icon;
        WatchDragSource(source);
        _seat.Pointer.StartGrab(_drag);
        DragChanged?.Invoke();
        DragStarted?.Invoke(new DragEvent(source, origin, icon));

        _drag.Enter(_seat.Pointer.Focus, _seat.Pointer.X, _seat.Pointer.Y);
    }

    private void OfferSelectionTo(Surface? surface)
    {
        if (surface is null || _seat.ClientOf(surface) is not { } client)
        {
            return;
        }

        foreach (var device in client.DataDevices)
        {
            if (Selection is { IsDestroyed: false } selection)
            {
                var offer = CreateOffer(device, selection, dnd: false);
                device.SendSelection(offer);
            }
            else
            {
                device.SendSelection(null);
            }
        }
    }

    private WlDataOfferResource CreateOffer(WlDataDeviceResource device, DataSource source, bool dnd)
    {
        var offer = new WlDataOfferResource(device.Client, device.Version, 0);
        device.SendDataOffer(offer);
        foreach (var mime in source.MimeTypes)
        {
            offer.SendOffer(mime);
        }

        string? acceptedMime = null;
        offer.Accept += (_, e) =>
        {
            if (dnd && acceptedMime != e.MimeType)
            {
                source.Target(e.MimeType);
            }

            acceptedMime = e.MimeType;
        };

        offer.Receive += (_, e) =>
        {
            if (!dnd && Store is { } store)
            {
                store.Receive(Capabilities.SelectionKind.Clipboard, e.MimeType, new ClientFd(e.Fd, offer.Client));
                return;
            }

            source.Send(e.MimeType, new ClientFd(e.Fd, offer.Client));
        };

        if (dnd && offer.Version >= 3)
        {
            offer.SetActions += (_, e) => _drag?.NegotiateActions(
                offer, source, (WlDataDeviceManager.DndAction)e.DndActions, (WlDataDeviceManager.DndAction)e.PreferredAction);
            offer.Finish += (_, _) => source.Finished();
        }

        return offer;
    }

    private sealed class DragGrab : IPointerGrab
    {
        private readonly Seat _seat;
        private readonly SeatDataDevice _owner;
        private readonly DataSource? _source;
        private Surface? _focus;
        private WlDataOfferResource? _offer;
        private WlDataDeviceResource? _focusDevice;
        private bool _accepted;
        private WlDataDeviceManager.DndAction? _lastAction;

        internal DragGrab(Seat seat, SeatDataDevice owner, DataSource? source)
        {
            _seat = seat;
            _owner = owner;
            _source = source;
        }

        internal DataSource? Source => _source;

        public void Enter(Surface? surface, double x, double y)
        {
            if (surface is not null && ReferenceEquals(surface, _owner.DraggingIcon))
            {
                surface = null;
            }

            if (_focus == surface)
            {
                return;
            }

            if (_focusDevice is { IsDestroyed: false })
            {
                _focusDevice.SendLeave();
            }

            _focus = surface;
            _offer = null;
            _focusDevice = null;
            _accepted = false;
            _lastAction = null;

            if (surface is null || _seat.ClientOf(surface) is not { } client || client.DataDevices.Count == 0)
            {
                return;
            }

            var device = client.DataDevices[0];
            _focusDevice = device;
            var serial = _seat.NextSerial(SerialKind.Other);
            if (_source is { IsDestroyed: false } source)
            {
                _offer = _owner.CreateOffer(device, source, dnd: true);
                _offer.Accept += (_, e) => _accepted = e.MimeType is not null;
                if (_offer.Version >= 3 && source.Actions != 0)
                {
                    _offer.SendSourceActions(source.Actions);
                }

                device.SendEnter(serial, surface.Resource, WlFixed.FromDouble(x), WlFixed.FromDouble(y), _offer);
            }
            else
            {
                device.SendEnter(serial, surface.Resource, WlFixed.FromDouble(x), WlFixed.FromDouble(y), null);
            }
        }

        public void Motion(uint timeMs, double x, double y)
        {
            if (_focusDevice is { IsDestroyed: false })
            {
                _focusDevice.SendMotion(timeMs, WlFixed.FromDouble(x), WlFixed.FromDouble(y));
            }
        }

        public uint Button(uint timeMs, uint button, WlPointer.ButtonState state)
        {
            if (state == WlPointer.ButtonState.Released)
            {
                End(_accepted && _focusDevice is { IsDestroyed: false }
                    ? Capabilities.DragOutcome.Dropped
                    : Capabilities.DragOutcome.Cancelled);
            }

            return 0;
        }

        public void Axis(uint timeMs, in PointerAxis axis)
        {
        }

        public void Cancel() => End(Capabilities.DragOutcome.Cancelled);

        internal void NegotiateActions(WlDataOfferResource offer, DataSource source, WlDataDeviceManager.DndAction offered, WlDataDeviceManager.DndAction preferred)
        {
            var shared = offered & source.Actions;
            var action = (preferred & shared) != 0 ? preferred
                : (shared & WlDataDeviceManager.DndAction.Copy) != 0 ? WlDataDeviceManager.DndAction.Copy
                : (shared & WlDataDeviceManager.DndAction.Move) != 0 ? WlDataDeviceManager.DndAction.Move
                : WlDataDeviceManager.DndAction.None;
            if (_lastAction == action)
            {
                return;
            }

            _lastAction = action;
            offer.SendAction(action);
            source.Action(action);
        }

        internal void End(Capabilities.DragOutcome outcome)
        {
            if (_owner._drag != this)
            {
                return;
            }

            if (outcome == Capabilities.DragOutcome.Dropped && _focusDevice is { IsDestroyed: false })
            {
                _focusDevice.SendDrop();
                _source?.DropPerformed();
            }
            else
            {
                if (_focusDevice is { IsDestroyed: false })
                {
                    _focusDevice.SendLeave();
                }

                if (outcome != Capabilities.DragOutcome.Handed)
                {
                    _source?.Cancel();
                }
            }

            _owner._drag = null;
            _owner._dragIcon = null;
            _seat.Pointer.EndGrab(this);
            _owner.DragChanged?.Invoke();
            _owner.DragEnded?.Invoke();

            _seat.Pointer.SendEnter(_focus, _seat.Pointer.X, _seat.Pointer.Y);
        }
    }
}
