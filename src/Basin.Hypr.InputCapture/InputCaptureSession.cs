using Basin.Diagnostics;
using Basin.Hypr.InputCapture.Protocol;
using Wayland;
using static Basin.Hypr.InputCapture.InputCaptureLog;

namespace Basin.Hypr.InputCapture;

internal sealed class InputCaptureSession : IDisposable
{
    private enum Status
    {
        Created,
        Enabled,
        Activated,
    }

    private readonly HyprlandInputCaptureManager _owner;
    private readonly HyprlandInputCaptureV1Resource _resource;
    private readonly List<InputCaptureBarrier> _barriers = [];
    private readonly IEventSource _repeatTimer;
    private Status _status = Status.Created;
    private uint _activationId;
    private uint _repeatKey;
    private bool _repeating;
    private bool _disposed;

    public InputCaptureSession(
        HyprlandInputCaptureManager owner,
        HyprlandInputCaptureV1Resource resource,
        string handle,
        EisBridge eis)
    {
        _owner = owner;
        _resource = resource;
        Handle = handle;
        Eis = eis;
        _repeatTimer = owner.Loop.AddTimer(OnRepeat);
        BasinCounters.Track();

        resource.Enable += (_, _) => OnEnable();
        resource.Disable += (_, _) => Disable();
        resource.ClearBarriers += (_, _) => _barriers.Clear();
        resource.AddBarrier += (_, e) => OnAddBarrier(e.ZoneSet, e.Id, e.X1, e.Y1, e.X2, e.Y2);
        resource.Release += (_, e) => OnRelease(e.ActivationId, e.X.ToDouble(), e.Y.ToDouble());
        resource.Destroyed += (_, _) =>
        {
            Dispose();
            _owner.Forget(this);
        };
    }

    public string Handle { get; }

    public EisBridge Eis { get; }

    public bool IsEnabled => _status == Status.Enabled;

    public bool IsActivated => _status == Status.Activated;

    public uint ActivationId => _activationId;

    public IReadOnlyList<InputCaptureBarrier> Barriers => _barriers;

    public bool Activate(double x, double y, uint barrierId)
    {
        if (_status != Status.Enabled)
        {
            return false;
        }

        _activationId++;
        _status = Status.Activated;
        Eis.StartEmulating(_activationId);
        if (!_resource.IsDestroyed)
        {
            _resource.SendActivated(_activationId, WlFixed.FromDouble(x), WlFixed.FromDouble(y), barrierId);
        }

        Log.Info($"session {Handle} captured input, activation {_activationId}, barrier {barrierId}");
        return true;
    }

    public void Deactivate()
    {
        if (_status != Status.Activated)
        {
            return;
        }

        StopRepeat();
        _status = Status.Enabled;
        Eis.StopEmulating();
        _owner.Released(this);
        if (!_resource.IsDestroyed)
        {
            _resource.SendDeactivated(_activationId);
        }

        Log.Info($"session {Handle} released input");
    }

    public void Disable()
    {
        StopRepeat();
        if (_status == Status.Activated)
        {
            Deactivate();
        }

        if (_status != Status.Enabled)
        {
            return;
        }

        _status = Status.Created;
        if (!_resource.IsDestroyed)
        {
            _resource.SendDisabled();
        }
    }

    public void ClearBarriers() => _barriers.Clear();

    public void Motion(double dx, double dy) => Eis.SendMotion(dx, dy);

    public void Button(uint button, bool pressed) => Eis.SendButton(button, pressed);

    public void Axis(in PointerAxis axis) => Eis.SendScroll(in axis);

    public void Key(uint key, bool pressed)
    {
        Eis.SendKey(key, pressed);
        if (pressed)
        {
            StartRepeat(key);
        }
        else if (_repeating && _repeatKey == key)
        {
            StopRepeat();
        }
    }

    public void Modifiers(uint depressed, uint latched, uint locked, uint group) =>
        Eis.SendModifiers(depressed, latched, locked, group);

    public void KeymapChanged()
    {
        StopRepeat();
        Eis.ResetKeyboard();
    }

    public void LayoutChanged()
    {
        _barriers.Clear();
        Disable();
        Eis.ResetPointer();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRepeat();
        _repeatTimer.Remove();
        if (_status == Status.Activated)
        {
            Deactivate();
        }

        _barriers.Clear();
        Eis.Dispose();
        BasinCounters.Untrack();
    }

    private void OnEnable() => _status = _status == Status.Activated ? _status : Status.Enabled;

    private void OnAddBarrier(uint zoneSet, uint id, uint x1, uint y1, uint x2, uint y2)
    {
        _ = zoneSet;
        var barrier = InputCaptureBarriers.FromWire(id, x1, y1, x2, y2);
        foreach (var existing in _barriers)
        {
            if (existing.Id == id)
            {
                _resource.PostError((uint)HyprlandInputCaptureV1.Error.InvalidBarrierId, $"barrier {id} already exists");
                return;
            }
        }

        if (!InputCaptureBarriers.IsValid(in barrier, _owner.Layout))
        {
            Log.Info($"session {Handle} barrier {id} [{barrier.X1},{barrier.Y1}]-[{barrier.X2},{barrier.Y2}] is invalid");
            if (_owner.EnforceBarriers)
            {
                _resource.PostError((uint)HyprlandInputCaptureV1.Error.InvalidBarrier, $"barrier {id} is not an output edge");
                return;
            }
        }

        _barriers.Add(barrier);
    }

    private void OnRelease(uint activationId, double x, double y)
    {
        if (_status == Status.Activated && activationId != _activationId)
        {
            _resource.PostError(
                (uint)HyprlandInputCaptureV1.Error.InvalidActivationId,
                $"activation id {activationId} is not the current {_activationId}");
            return;
        }

        Deactivate();
        if (x != -1 && y != -1)
        {
            _owner.RequestWarp(x, y);
        }
    }

    private void StartRepeat(uint key)
    {
        var keyboard = _owner.Seat.Keyboard;
        var (rate, delay) = keyboard.RepeatInfo;
        if (rate <= 0 || keyboard.Keymap is not { } keymap || !keymap.KeyRepeats(key + 8))
        {
            StopRepeat();
            return;
        }

        _repeating = true;
        _repeatKey = key;
        _repeatTimer.UpdateTimer(Math.Max(0, delay));
    }

    private void StopRepeat()
    {
        _repeating = false;
        if (!_repeatTimer.IsRemoved)
        {
            _repeatTimer.UpdateTimer(0);
        }
    }

    private void OnRepeat()
    {
        if (!_repeating || _status != Status.Activated)
        {
            return;
        }

        Eis.SendKey(_repeatKey, false);
        Eis.SendKey(_repeatKey, true);
        var (rate, _) = _owner.Seat.Keyboard.RepeatInfo;
        if (rate > 0)
        {
            _repeatTimer.UpdateTimer(Math.Max(1, 1000 / rate));
        }
    }
}
