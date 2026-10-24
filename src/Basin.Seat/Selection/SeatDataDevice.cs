using Wayland;

namespace Basin.Seat;

/// <summary>
/// Runs the core clipboard, the primary selection and drag-and-drop for one seat.
/// It is the default <see cref="Capabilities.IDragTracker"/>.
/// </summary>
public sealed class SeatDataDevice : Capabilities.IDragTracker
{
    /// <summary>
    /// The surface role that a drag icon takes. A surface with a different role cannot be a drag icon.
    /// </summary>
    public const string IconRole = "dnd_icon";

    private readonly Seat _seat;
    private DragGrab? _drag;
    private Surface? _dragIcon;

    internal SeatDataDevice(Seat seat)
    {
        _seat = seat;
    }

    /// <summary>
    /// Gets or sets the store that the core data device routes the clipboard through.
    /// When it is <see langword="null"/>, a client <c>set_selection</c> calls <see cref="SetSelection"/> and a paste reads the source directly.
    /// </summary>
    public Capabilities.ISelectionStore? Store { get; set; }

    /// <summary>
    /// Gets the clipboard source, or <see langword="null"/> when the clipboard is empty.
    /// It becomes <see langword="null"/> when its source is destroyed.
    /// </summary>
    public DataSource? Selection { get; private set; }

    /// <summary>
    /// Gets the primary selection source, or <see langword="null"/> when the primary selection is empty.
    /// It becomes <see langword="null"/> when its source is destroyed.
    /// </summary>
    public DataSource? PrimarySelection { get; private set; }

    /// <summary>
    /// Occurs after the clipboard source changes. The argument is the new source.
    /// It also occurs with <see langword="null"/> when the source is destroyed.
    /// </summary>
    public event Action<DataSource?>? SelectionChanged;

    /// <summary>
    /// Occurs after the primary selection changes. The argument is the new source.
    /// It also occurs with <see langword="null"/> when the source is destroyed.
    /// </summary>
    public event Action<DataSource?>? PrimarySelectionChanged;

    /// <summary>
    /// Occurs when a client sends <c>set_selection</c> on its data device. The argument is the source, or <see langword="null"/> to clear.
    /// When a handler is attached, the request goes to the handler only, and the seat does not change the clipboard.
    /// </summary>
    public event Action<DataSource?>? SelectionRequested;

    /// <summary>
    /// Occurs when a drag starts, after the pointer grab is installed and before the first enter.
    /// </summary>
    public event Action<DragEvent>? DragStarted;

    /// <summary>
    /// Occurs when a drag ends with any outcome, after the pointer grab is removed.
    /// </summary>
    public event Action? DragEnded;

    /// <inheritdoc/>
    public DataSource? DraggingSource => _drag?.Source;

    /// <inheritdoc/>
    public Surface? DraggingIcon => _drag is null ? null : _dragIcon;

    /// <inheritdoc/>
    public event Action? DragChanged;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void EndDrag(Capabilities.DragOutcome outcome) => _drag?.End(outcome);

    /// <summary>
    /// Sets the clipboard source and offers it to the client with keyboard focus.
    /// The old source gets a cancelled event. Setting the current source again does nothing.
    /// </summary>
    /// <param name="source">The new source, or <see langword="null"/> to clear the clipboard.</param>
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
                    SelectionChanged?.Invoke(null);
                }
            };
        }

        old?.Cancel();
        OfferSelectionTo(_seat.Keyboard.Focus);
        SelectionChanged?.Invoke(source);
    }

    /// <summary>
    /// Sets the primary selection source. The old source gets a cancelled event.
    /// Setting the current source again does nothing.
    /// </summary>
    /// <param name="source">The new source, or <see langword="null"/> to clear the primary selection.</param>
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

    internal void WireDevice(SeatClient seatClient)
    {
        if (_seat.Keyboard.Focus is { } focus && _seat.ClientOf(focus) == seatClient)
        {
            OfferSelectionTo(focus);
        }
    }

    internal void OnSetSelection(in WlDataDeviceResource.SetSelectionEventArgs e)
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
    }

    internal void HandleStartDrag(SeatClient seatClient, in WlDataDeviceResource.StartDragEventArgs e)
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
                var offer = CreateOffer(device, selection, dnd: false, grab: null);
                device.SendSelection(offer);
            }
            else
            {
                device.SendSelection(null);
            }
        }
    }

    private WlDataOfferResource CreateOffer(WlDataDeviceResource device, DataSource source, bool dnd, DragGrab? grab)
    {
        var offer = new WlDataOfferResource(device.Client, device.Version, 0, new DataOffer(this, source, dnd, grab));
        device.SendDataOffer(offer);
        foreach (var mime in source.MimeTypes)
        {
            offer.SendOffer(mime);
        }

        return offer;
    }

    private sealed class DataOffer(SeatDataDevice owner, DataSource source, bool dnd, DragGrab? grab) : IWlDataOfferHandler
    {
        private string? _acceptedMime;

        void IWlDataOfferHandler.Accept(WlDataOfferResource resource, in WlDataOfferResource.AcceptEventArgs args)
        {
            if (dnd && _acceptedMime != args.MimeType)
            {
                source.Target(args.MimeType);
            }

            _acceptedMime = args.MimeType;
            grab?.OnAccept(args.MimeType);
        }

        void IWlDataOfferHandler.Receive(WlDataOfferResource resource, in WlDataOfferResource.ReceiveEventArgs args)
        {
            if (!dnd && owner.Store is { } store)
            {
                store.Receive(Capabilities.SelectionKind.Clipboard, args.MimeType, new ClientFd(args.Fd, resource.Client));
                return;
            }

            source.Send(args.MimeType, new ClientFd(args.Fd, resource.Client));
        }

        void IWlDataOfferHandler.Destroy(WlDataOfferResource resource)
        {
        }

        void IWlDataOfferHandler.Finish(WlDataOfferResource resource)
        {
            if (dnd && resource.Version >= 3)
            {
                source.Finished();
            }
        }

        void IWlDataOfferHandler.SetActions(WlDataOfferResource resource, in WlDataOfferResource.SetActionsEventArgs args)
        {
            if (dnd && resource.Version >= 3)
            {
                owner._drag?.NegotiateActions(
                    resource, source, (WlDataDeviceManager.DndAction)args.DndActions, (WlDataDeviceManager.DndAction)args.PreferredAction);
            }
        }
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

        internal void OnAccept(string? mimeType) => _accepted = mimeType is not null;

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
                _offer = _owner.CreateOffer(device, source, dnd: true, grab: this);
                if (_offer.Version >= 3 && source.Actions != 0)
                {
                    _offer.SendSourceActions(source.Actions);
                }

                device.SendEnter(serial, surface.Resource, WlFixed.FromDouble(x * surface.ServerScale), WlFixed.FromDouble(y * surface.ServerScale), _offer);
            }
            else
            {
                device.SendEnter(serial, surface.Resource, WlFixed.FromDouble(x * surface.ServerScale), WlFixed.FromDouble(y * surface.ServerScale), null);
            }
        }

        public void Motion(uint timeMs, double x, double y)
        {
            if (_focusDevice is { IsDestroyed: false })
            {
                var scale = _focus?.ServerScale ?? 1.0;
                _focusDevice.SendMotion(timeMs, WlFixed.FromDouble(x * scale), WlFixed.FromDouble(y * scale));
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
