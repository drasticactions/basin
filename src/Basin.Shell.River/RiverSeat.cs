using Basin.Shell.River.Protocol;

namespace Basin.Shell.River;

internal sealed class RiverSeat
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

    internal void Bind(RiverSeatV1Resource resource)
    {
        Resource = resource;
        _pointerPositionSent = false;
        _pointerEnterValid = false;

        resource.FocusWindow += (_, e) =>
        {
            if (_manager.EnsureWindowing() && _manager.ResolveWindow(e.Window) is { } window)
            {
                RequestedFocus = FocusTarget.Window;
                RequestedFocusWindow = window;
            }
        };
        resource.ClearFocus += (_, _) =>
        {
            if (_manager.EnsureWindowing())
            {
                RequestedFocus = FocusTarget.None;
                RequestedFocusWindow = null;
            }
        };
        resource.OpStartPointer += (_, _) =>
        {
            if (!_manager.EnsureWindowing() || OperationActive || OperationPending)
            {
                return;
            }

            OperationPending = true;
        };
        resource.OpEnd += (_, _) =>
        {
            if (_manager.EnsureWindowing())
            {
                OperationActive = false;
                OperationReleased = false;
            }
        };
        resource.PointerWarp += (_, e) =>
        {
            if (_manager.EnsureWindowing())
            {
                PendingWarp = new Point(e.X, e.Y);
            }
        };
        resource.GetPointerBinding += (_, e) =>
        {
            var bindingResource = new RiverPointerBindingV1Resource(resource.Client, resource.Version, e.Id);
            var binding = new RiverPointerBinding(_manager, bindingResource, e.Button, e.Modifiers);
            _pointerBindings.Add(binding);
            bindingResource.DestroyRequest += (_, _) => _pointerBindings.Remove(binding);
        };
        resource.SetXcursorTheme += (_, e) => CursorTheme = (e.Name, (int)e.Size);
        resource.DestroyRequest += (_, _) => Resource = null;
    }

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
