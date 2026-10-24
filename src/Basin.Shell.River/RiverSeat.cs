using Basin.Shell.River.Protocol;
using Wayland.Server;

namespace Basin.Shell.River;

internal sealed class RiverSeat : IRiverSeatV1Handler
{
    private readonly RiverWindowManager _manager;
    private readonly List<RiverWindow> _interactions = [];
    private readonly List<RiverShellSurface> _shellInteractions = [];
    private Point _sentPointerPosition;
    private bool _pointerPositionSent;
    private RiverWindow? _pointerEnterSent;
    private bool _pointerEnterValid;

    internal RiverSeat(RiverWindowManager manager, Basin.Seat.Seat seat)
    {
        _manager = manager;
        Seat = seat;
    }

    internal Basin.Seat.Seat Seat { get; }

    internal RiverSeatV1Resource? Resource { get; private set; }

    internal Point PointerPosition { get; set; }

    internal RiverWindow? PointerFocus { get; set; }

    internal FocusTarget RequestedFocus { get; private set; }

    internal RiverWindow? RequestedFocusWindow { get; private set; }

    internal bool OperationActive { get; private set; }

    internal Point OperationOrigin { get; private set; }

    internal bool OperationReleased { get; private set; }

    internal Point? PendingWarp { get; private set; }

    internal bool OperationPending { get; private set; }

    internal void BeginPendingOperation()
    {
        if (!OperationPending)
        {
            return;
        }

        OperationPending = false;
        OperationActive = true;
        OperationReleased = false;
        OperationOrigin = PointerPosition;
        _sentDelta = default;
        _deltaSent = false;
    }

    internal RiverSeatV1Resource Bind(WlClient client, uint version, uint id)
    {
        var resource = new RiverSeatV1Resource(client, version, id, this);
        Resource = resource;
        _pointerPositionSent = false;
        _pointerEnterValid = false;
        return resource;
    }

    internal void ForgetPointerBinding(RiverPointerBinding binding) => _pointerBindings.Remove(binding);

    void IRiverSeatV1Handler.FocusWindow(RiverSeatV1Resource resource, in RiverSeatV1Resource.FocusWindowEventArgs args)
    {
        if (_manager.EnsureWindowing() && _manager.ResolveWindow(args.Window) is { } window)
        {
            RequestedFocus = FocusTarget.Window;
            RequestedFocusWindow = window;
        }
    }

    void IRiverSeatV1Handler.FocusShellSurface(RiverSeatV1Resource resource, in RiverSeatV1Resource.FocusShellSurfaceEventArgs args)
    {
    }

    void IRiverSeatV1Handler.ClearFocus(RiverSeatV1Resource resource)
    {
        if (_manager.EnsureWindowing())
        {
            RequestedFocus = FocusTarget.None;
            RequestedFocusWindow = null;
        }
    }

    void IRiverSeatV1Handler.OpStartPointer(RiverSeatV1Resource resource)
    {
        if (!_manager.EnsureWindowing() || OperationActive || OperationPending)
        {
            return;
        }

        OperationPending = true;
    }

    void IRiverSeatV1Handler.OpEnd(RiverSeatV1Resource resource)
    {
        if (_manager.EnsureWindowing())
        {
            OperationActive = false;
            OperationReleased = false;
        }
    }

    void IRiverSeatV1Handler.PointerWarp(RiverSeatV1Resource resource, in RiverSeatV1Resource.PointerWarpEventArgs args)
    {
        if (_manager.EnsureWindowing())
        {
            PendingWarp = new Point(args.X, args.Y);
        }
    }

    void IRiverSeatV1Handler.GetPointerBinding(RiverSeatV1Resource resource, in RiverSeatV1Resource.GetPointerBindingEventArgs args)
    {
        var binding = new RiverPointerBinding(_manager, this, resource.Client, resource.Version, args.Id, args.Button, args.Modifiers);
        _pointerBindings.Add(binding);
    }

    void IRiverSeatV1Handler.SetXcursorTheme(RiverSeatV1Resource resource, in RiverSeatV1Resource.SetXcursorThemeEventArgs args) =>
        CursorTheme = (args.Name, (int)args.Size);

    void IRiverSeatV1Handler.Destroy(RiverSeatV1Resource resource) => Resource = null;

    internal (string Name, int Size)? CursorTheme { get; private set; }

    internal void ReportInteraction(RiverWindow window)
    {
        if (!_interactions.Contains(window))
        {
            _interactions.Add(window);
        }

        _manager.MarkManageDirty();
    }

    internal void ReportInteraction(RiverShellSurface shell)
    {
        if (!_shellInteractions.Contains(shell))
        {
            _shellInteractions.Add(shell);
        }

        _manager.MarkManageDirty();
    }

    internal void SendChanges(uint version)
    {
        if (Resource is not { IsDestroyed: false } resource)
        {
            return;
        }

        if (version >= 2 && (!_pointerPositionSent || _sentPointerPosition != PointerPosition))
        {
            _pointerPositionSent = true;
            _sentPointerPosition = PointerPosition;
            resource.SendPointerPosition(PointerPosition.X, PointerPosition.Y);
        }

        if (!_pointerEnterValid || !ReferenceEquals(_pointerEnterSent, PointerFocus))
        {
            if (_pointerEnterValid && _pointerEnterSent is not null)
            {
                resource.SendPointerLeave();
            }

            if (PointerFocus?.Resource is { } entered)
            {
                resource.SendPointerEnter(entered);
            }

            _pointerEnterSent = PointerFocus;
            _pointerEnterValid = true;
        }

        if (OperationActive)
        {
            var delta = new Point(
                PointerPosition.X - OperationOrigin.X,
                PointerPosition.Y - OperationOrigin.Y);
            if (!_deltaSent || _sentDelta != delta)
            {
                _deltaSent = true;
                _sentDelta = delta;

                resource.SendOpDelta(delta.X, delta.Y);
            }

            if (OperationReleased && !_releaseSent)
            {
                _releaseSent = true;
                resource.SendOpRelease();
            }
        }
        else
        {
            _releaseSent = false;
        }

        foreach (var window in _interactions)
        {
            if (window.Resource is { IsDestroyed: false } target)
            {
                resource.SendWindowInteraction(target);
            }
        }

        _interactions.Clear();

        foreach (var shell in _shellInteractions)
        {
            if (!shell.IsDestroyed && shell.Resource is { IsDestroyed: false } target)
            {
                resource.SendShellSurfaceInteraction(target);
            }
        }

        _shellInteractions.Clear();
    }

    private Point _sentDelta;
    private bool _deltaSent;
    private bool _releaseSent;

    private readonly List<RiverPointerBinding> _pointerBindings = [];
    private readonly Dictionary<uint, RiverPointerBinding> _heldButtons = [];

    internal bool HandleButton(uint button, bool pressed, RiverSeatV1.Modifiers modifiers)
    {
        if (!pressed)
        {
            if (_heldButtons.Remove(button, out var held))
            {
                held.SendReleased();
                _manager.MarkManageDirty();
                return true;
            }

            return false;
        }

        foreach (var binding in _pointerBindings)
        {
            if (binding.IsEnabled && binding.Button == button && binding.Modifiers == modifiers)
            {
                _heldButtons[button] = binding;
                binding.SendPressed();
                _manager.MarkManageDirty();
                return true;
            }
        }

        return false;
    }

    internal void ReportOperationReleased()
    {
        if (OperationActive && !OperationReleased)
        {
            OperationReleased = true;
            _manager.MarkManageDirty();
        }
    }

    internal void ClearFocusRequest()
    {
        RequestedFocus = FocusTarget.Unchanged;
        RequestedFocusWindow = null;
        PendingWarp = null;
    }

    internal void ResetForNewManager()
    {
        Resource = null;
        RequestedFocus = FocusTarget.Unchanged;
        RequestedFocusWindow = null;
        OperationActive = false;
        OperationPending = false;
        OperationReleased = false;
        PendingWarp = null;
        CursorTheme = null;
        _interactions.Clear();
        _shellInteractions.Clear();
        _pointerBindings.Clear();
        _heldButtons.Clear();
    }
}
