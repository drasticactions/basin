using Basin.Diagnostics;
using static Basin.Eis.EisLog;

namespace Basin.Eis;

public sealed class InputCaptureSession : IDisposable
{
    private enum Status
    {
        Created,
        Enabled,
        Activated,
    }

    private readonly InputCaptureEngine _owner;
    private readonly IInputCaptureFront _front;
    private readonly List<InputCaptureBarrier> _barriers = [];
    private readonly IEventSource _repeatTimer;
    private Status _status = Status.Created;
    private uint _activationId;
    private uint _repeatKey;
    private bool _repeating;
    private bool _disposed;

    internal InputCaptureSession(InputCaptureEngine owner, IInputCaptureFront front, string handle, EisSender eis)
    {
        _owner = owner;
        _front = front;
        Handle = handle;
        Eis = eis;
        _repeatTimer = owner.Loop.AddTimer(OnRepeat);
        BasinCounters.Track();
    }

    public string Handle { get; }

    public EisSender Eis { get; }

    public IInputCaptureFront Front => _front;

    public bool IsEnabled => _status == Status.Enabled;

    public bool IsActivated => _status == Status.Activated;

    public uint ActivationId => _activationId;

    public IReadOnlyList<InputCaptureBarrier> Barriers => _barriers;

    public void Enable() => _status = _status == Status.Activated ? _status : Status.Enabled;

    public bool Activate(double x, double y, uint barrierId)
    {
        if (_status != Status.Enabled)
        {
            return false;
        }

        _activationId++;
        _status = Status.Activated;
        Eis.StartEmulating(_activationId);
        _front.Activated(_activationId, x, y, barrierId);
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
        _front.Deactivated(_activationId);
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
        _front.Disabled();
    }

    public bool Release(uint activationId, double x, double y)
    {
        if (_status == Status.Activated && activationId != _activationId)
        {
            return false;
        }

        Deactivate();
        if (x != -1 && y != -1)
        {
            _owner.RequestWarp(x, y);
        }

        return true;
    }

    public void ClearBarriers() => _barriers.Clear();

    public bool HasBarrier(uint id)
    {
        foreach (var existing in _barriers)
        {
            if (existing.Id == id)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryAddBarrier(in InputCaptureBarrier barrier)
    {
        if (HasBarrier(barrier.Id))
        {
            return false;
        }

        if (!InputCaptureBarriers.IsValid(in barrier, _owner.Layout))
        {
            Log.Info($"session {Handle} barrier {barrier.Id} [{barrier.X1},{barrier.Y1}]-[{barrier.X2},{barrier.Y2}] is invalid");
            if (_owner.EnforceBarriers)
            {
                return false;
            }
        }

        _barriers.Add(barrier);
        return true;
    }

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

    internal void KeymapChanged()
    {
        StopRepeat();
        Eis.ResetKeyboard();
    }

    internal void LayoutChanged()
    {
        _barriers.Clear();
        Disable();
        Eis.ResetPointer();
        _front.ZonesChanged();
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
        _owner.Forget(this);
        BasinCounters.Untrack();
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
