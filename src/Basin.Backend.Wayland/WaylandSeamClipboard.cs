using Basin.Backend.Wayland.Protocol;
using Basin.Capabilities;
using Wayland;
using static Basin.Backend.Wayland.WaylandBackendLog;

namespace Basin.Backend.Wayland;

internal sealed class WaylandSeamClipboard : WaylandSeamSelectionBridge
{
    private readonly WlDataDevice _device;
    private readonly IDragTracker? _drags;
    private WlDataOffer? _pending;
    private List<string>? _pendingMimeTypes;
    private WlDataOffer? _current;
    private WlDataSource? _source;
    private WlDataOffer? _dragOffer;
    private DataSource? _dragSource;
    private WaylandOutput? _dragOutput;
    private bool _dropped;
    private WlDataSource? _outSource;
    private DataSource? _outGuest;
    private DragOutcome? _outOutcome;
    private bool _endingOutbound;
    private bool _selfEntered;
    private bool _serialWarned;
    private bool _localOnlyWarned;
    private ParentDragIcon? _outIcon;

    internal WaylandSeamClipboard(WaylandBackend backend, ISelectionStore store, WlDataDevice device, IDragTracker? drags)
        : base(backend, store, SelectionKind.Clipboard)
    {
        _device = device;
        _drags = drags;
        _device.DataOffer += OnParentDataOffer;
        _device.Selection += OnParentSelection;
        if (drags is not null)
        {
            _device.Enter += OnParentDragEnter;
            _device.Motion += OnParentDragMotion;
            _device.Leave += OnParentDragLeave;
            _device.Drop += OnParentDrop;
            drags.DragChanged += OnGuestDragChanged;
        }
    }

    internal Action<WaylandOutput, uint, double, double>? PointerMotion { get; set; }

    public override void Dispose()
    {
        _device.DataOffer -= OnParentDataOffer;
        _device.Selection -= OnParentSelection;
        if (_drags is not null)
        {
            _device.Enter -= OnParentDragEnter;
            _device.Motion -= OnParentDragMotion;
            _device.Leave -= OnParentDragLeave;
            _device.Drop -= OnParentDrop;
            _drags.DragChanged -= OnGuestDragChanged;
        }

        DisposeOutbound();
        EndHostDrag(dropped: false);
        DropParentSource();
        DiscardPending();
        WaylandBackend.DisposeParent(_current);
        _current = null;
        base.Dispose();
    }

    private void OnParentDragEnter(object? sender, WlDataDevice.EnterEventArgs e)
    {
        if (_outSource is { IsDestroyed: false })
        {
            OnOwnDragEntered(e);
            return;
        }

        EndHostDrag(dropped: false);
        if (e.Id is null ||
            !ReferenceEquals(e.Id, _pending) ||
            _pendingMimeTypes is not { Count: > 0 } mimeTypes ||
            Backend.FindOutput(e.Surface) is not { } output)
        {
            return;
        }

        _dragOffer = e.Id;
        _pending = null;
        _pendingMimeTypes = null;
        _dragOutput = output;
        _dropped = false;

        if (e.Id.SupportsSetActions)
        {
            e.Id.SetActions(
                WlDataDeviceManager.DndAction.Copy | WlDataDeviceManager.DndAction.Move,
                WlDataDeviceManager.DndAction.Copy);
        }

        e.Id.Accept(e.Serial, mimeTypes[0]);
        Backend.Flush();

        _dragSource = new DataSource(mimeTypes, SendFromHostDrag);
        var factor = output.SurfaceToPhysical;
        PointerMotion?.Invoke(output, 0, e.X.ToDouble() * factor, e.Y.ToDouble() * factor);
        _drags!.StartDrag(_dragSource);
    }

    private void OnParentDragMotion(object? sender, WlDataDevice.MotionEventArgs e)
    {
        if (_dragOutput is not { } output)
        {
            return;
        }

        var factor = output.SurfaceToPhysical;
        PointerMotion?.Invoke(output, e.Time, e.X.ToDouble() * factor, e.Y.ToDouble() * factor);
    }

    private void OnParentDragLeave(object? sender, WlDataDevice.LeaveEventArgs e)
    {
        if (_selfEntered)
        {
            _selfEntered = false;
            _dragOutput = null;
            WaylandBackend.DisposeParent(_dragOffer);
            _dragOffer = null;
            Backend.Flush();
            return;
        }

        if (!_dropped)
        {
            EndHostDrag(dropped: false);
        }
    }

    private void OnParentDrop(object? sender, WlDataDevice.DropEventArgs e)
    {
        if (_selfEntered)
        {
            EndOutboundGrab(DragOutcome.Dropped);
            ReleaseParentButtons();
            if (_dragOffer is { IsDestroyed: false, SupportsFinish: true } offer)
            {
                offer.Finish();
                Backend.Flush();
            }

            return;
        }

        if (_dragSource is null)
        {
            return;
        }

        _dropped = true;
        _drags!.EndDrag(DragOutcome.Dropped);
    }

    private void SendFromHostDrag(string mimeType, ClientFd fd)
    {
        if (IsDisposed || _dragOffer is not { IsDestroyed: false } offer)
        {
            fd.Close();
            return;
        }

        offer.Receive(mimeType, fd.Value);
        Backend.Flush();
        fd.Close();
        if (_dropped && offer.SupportsFinish)
        {
            _dropped = false;
            offer.Finish();
            Backend.Flush();
        }
    }

    private void OnOwnDragEntered(WlDataDevice.EnterEventArgs e)
    {
        WaylandBackend.DisposeParent(_dragOffer);
        _dragOffer = null;
        _selfEntered = false;
        if (e.Id is null ||
            !ReferenceEquals(e.Id, _pending) ||
            Backend.FindOutput(e.Surface) is not { } output ||
            _outGuest is not { } guest)
        {
            DiscardPending();
            return;
        }

        _pending = null;
        _pendingMimeTypes = null;
        _dragOffer = e.Id;
        _dragOutput = output;
        _selfEntered = true;
        if (e.Id.SupportsSetActions)
        {
            e.Id.SetActions(
                WlDataDeviceManager.DndAction.Copy | WlDataDeviceManager.DndAction.Move,
                WlDataDeviceManager.DndAction.Copy);
        }

        if (guest.MimeTypes.Count > 0)
        {
            e.Id.Accept(e.Serial, guest.MimeTypes[0]);
        }

        Backend.Flush();
        var factor = output.SurfaceToPhysical;
        PointerMotion?.Invoke(output, 0, e.X.ToDouble() * factor, e.Y.ToDouble() * factor);
    }

    private void OnGuestDragChanged()
    {
        if (IsDisposed || _endingOutbound || _drags is null)
        {
            return;
        }

        var guest = _drags.DraggingSource;
        if (guest is null)
        {
            DisposeOutbound();
            return;
        }

        if (ReferenceEquals(guest, _dragSource) || _outGuest is not null)
        {
            return;
        }

        StartOutbound(guest);
    }

    private void StartOutbound(DataSource guest)
    {
        if (Backend.ParentDataDeviceManager is not { } manager || guest.MimeTypes.Count == 0)
        {
            return;
        }

        if (Backend.Pointer?.CurrentOutput is not { } output)
        {
            if (!_localOnlyWarned)
            {
                _localOnlyWarned = true;
                Log.Info(
                    $"wayland backend: a guest drag began with the pointer outside every window; it stays inside this compositor");
            }

            return;
        }

        if (Backend.LastPointerButtonSerial is not { } serial || serial == 0)
        {
            if (!_serialWarned)
            {
                _serialWarned = true;
                Log.Info(
                    $"wayland backend: a guest drag stays inside this window; the parent issued no button serial to start one with");
            }

            return;
        }

        var source = manager.CreateDataSource();
        foreach (var mimeType in guest.MimeTypes)
        {
            source.Offer(mimeType);
        }

        if (source.SupportsSetActions)
        {
            var actions = guest.Actions;
            source.SetActions(actions == 0
                ? WlDataDeviceManager.DndAction.Copy | WlDataDeviceManager.DndAction.Move
                : actions);
        }

        source.Target += OnOutboundTarget;
        source.Action += OnOutboundAction;
        source.Send += OnOutboundSend;
        source.Cancelled += OnOutboundCancelled;
        source.DndDropPerformed += OnOutboundDropPerformed;
        source.DndFinished += OnOutboundFinished;

        _outSource = source;
        _outGuest = guest;
        _outOutcome = null;
        if (_drags!.DraggingIcon is { } icon)
        {
            _outIcon = new ParentDragIcon(Backend, icon);
        }

        _device.StartDrag(source, output.ParentSurface, _outIcon?.Surface, serial);
        Backend.Flush();
        _outIcon?.Mirror();
    }

    private void OnOutboundTarget(object? sender, WlDataSource.TargetEventArgs e) => _outGuest?.Target(e.MimeType);

    private void OnOutboundAction(object? sender, WlDataSource.ActionEventArgs e) => _outGuest?.Action(e.DndAction);

    private void OnOutboundSend(object? sender, WlDataSource.SendEventArgs e)
    {
        var fd = new ClientFd(e.Fd, null);
        if (_outGuest is { IsDestroyed: false } guest)
        {
            guest.Send(e.MimeType, fd);
            return;
        }

        fd.Close();
    }

    private void OnOutboundDropPerformed(object? sender, WlDataSource.DndDropPerformedEventArgs e)
    {
        if (_outOutcome is not null)
        {
            return;
        }

        _outGuest?.DropPerformed();
        EndOutboundGrab(DragOutcome.Handed);
        ReleaseParentButtons();
    }

    private void OnOutboundFinished(object? sender, WlDataSource.DndFinishedEventArgs e)
    {
        if (_outOutcome == DragOutcome.Handed)
        {
            _outGuest?.Finished();
        }

        DisposeOutbound();
    }

    private void OnOutboundCancelled(object? sender, WlDataSource.CancelledEventArgs e)
    {
        var handed = _outOutcome == DragOutcome.Handed;
        EndOutboundGrab(DragOutcome.Cancelled);
        ReleaseParentButtons();
        if (handed)
        {
            _outGuest?.Cancel();
        }

        DisposeOutbound();
    }

    private void ReleaseParentButtons() =>
        Backend.Pointer?.ReleaseHeldButtons((uint)Environment.TickCount);

    private void EndOutboundGrab(DragOutcome outcome)
    {
        if (_outOutcome is not null || _outGuest is null)
        {
            return;
        }

        _outOutcome = outcome;
        _endingOutbound = true;
        try
        {
            _drags!.EndDrag(outcome);
        }
        finally
        {
            _endingOutbound = false;
        }
    }

    private void DisposeOutbound()
    {
        if (_outSource is null && _outGuest is null)
        {
            return;
        }

        EndOutboundGrab(DragOutcome.Cancelled);
        var source = _outSource;
        _outSource = null;
        _outIcon?.Dispose();
        _outIcon = null;
        _outGuest = null;
        _outOutcome = null;
        _selfEntered = false;
        if (source is not null)
        {
            source.Target -= OnOutboundTarget;
            source.Action -= OnOutboundAction;
            source.Send -= OnOutboundSend;
            source.Cancelled -= OnOutboundCancelled;
            source.DndDropPerformed -= OnOutboundDropPerformed;
            source.DndFinished -= OnOutboundFinished;
            WaylandBackend.DisposeParent(source);
        }

        WaylandBackend.DisposeParent(_dragOffer);
        _dragOffer = null;
        _dragOutput = null;
        Backend.Flush();
    }

    private void EndHostDrag(bool dropped)
    {
        if (_dragSource is null)
        {
            return;
        }

        var source = _dragSource;
        _dragSource = null;
        _dragOutput = null;
        _dropped = false;
        _drags?.EndDrag(dropped ? DragOutcome.Dropped : DragOutcome.Cancelled);
        source.MarkDestroyed();
        WaylandBackend.DisposeParent(_dragOffer);
        _dragOffer = null;
        Backend.Flush();
    }

    protected override void SendFromParent(string mimeType, ClientFd fd)
    {
        if (IsDisposed || _current is not { IsDestroyed: false } offer)
        {
            fd.Close();
            return;
        }

        offer.Receive(mimeType, fd.Value);
        Backend.Flush();
        fd.Close();
    }

    protected override void PushToParent(ReadOnlySpan<string> mimeTypes, uint serial)
    {
        if (IsDisposed || Backend.ParentDataDeviceManager is not { } manager)
        {
            return;
        }

        DropParentSource();
        var source = manager.CreateDataSource();
        _source = source;
        foreach (var mimeType in mimeTypes)
        {
            source.Offer(mimeType);
        }

        source.Send += OnParentSourceSend;
        source.Cancelled += OnParentSourceCancelled;
        _device.SetSelection(source, serial);
        Backend.Flush();
    }

    protected override void DropParentSource()
    {
        if (_source is { } source)
        {
            source.Send -= OnParentSourceSend;
            source.Cancelled -= OnParentSourceCancelled;
            WaylandBackend.DisposeParent(source);
            Backend.Flush();
        }

        _source = null;
    }

    private void OnParentSourceSend(object? sender, WlDataSource.SendEventArgs e) =>
        ReceiveForParent(e.MimeType, e.Fd);

    private void OnParentSourceCancelled(object? sender, WlDataSource.CancelledEventArgs e) => DropParentSource();

    private void OnParentDataOffer(object? sender, WlDataDevice.DataOfferEventArgs e)
    {
        DiscardPending();
        var mimeTypes = new List<string>();
        _pending = e.Id;
        _pendingMimeTypes = mimeTypes;
        e.Id.Offer += (_, offered) => mimeTypes.Add(offered.MimeType);
    }

    private void OnParentSelection(object? sender, WlDataDevice.SelectionEventArgs e)
    {
        if (_source is { IsDestroyed: false })
        {
            DiscardPending();
            WaylandBackend.DisposeParent(_current);
            _current = null;
            return;
        }

        var replaced = _current;
        _current = null;
        if (e.Id is null || !ReferenceEquals(e.Id, _pending) || _pendingMimeTypes is not { Count: > 0 } mimeTypes)
        {
            DiscardPending();
            RetractFromGuests();
            WaylandBackend.DisposeParent(replaced);
            return;
        }

        _current = e.Id;
        _pending = null;
        _pendingMimeTypes = null;
        WaylandBackend.DisposeParent(replaced);
        OfferToGuests(mimeTypes);
    }

    private void DiscardPending()
    {
        WaylandBackend.DisposeParent(_pending);
        _pending = null;
        _pendingMimeTypes = null;
    }
}
