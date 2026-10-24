using Basin.Diagnostics;
using static Basin.Eis.EisLog;

namespace Basin.Eis;

/// <summary>
/// Holds one client's input capture state: its barriers, its EIS sender and its created, enabled or activated status. <see cref="InputCaptureEngine.CreateSession"/> makes it.
/// </summary>
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

    /// <summary>
    /// Gets the session handle that the front gave at creation.
    /// </summary>
    public string Handle { get; }

    /// <summary>
    /// Gets the EIS sender that sends the captured input to the client. The session owns it and disposes it.
    /// </summary>
    public EisSender Eis { get; }

    /// <summary>
    /// Gets the front that receives this session's state changes.
    /// </summary>
    public IInputCaptureFront Front => _front;

    /// <summary>
    /// Gets a value indicating whether the session is enabled and waits for a barrier crossing. It is <see langword="false"/> while the session is activated.
    /// </summary>
    public bool IsEnabled => _status == Status.Enabled;

    /// <summary>
    /// Gets a value indicating whether the session captures the input now.
    /// </summary>
    public bool IsActivated => _status == Status.Activated;

    /// <summary>
    /// Gets the id of the last activation, or 0 before the first one.
    /// </summary>
    public uint ActivationId => _activationId;

    /// <summary>
    /// Gets the barriers that the session holds now. A layout change clears them.
    /// </summary>
    public IReadOnlyList<InputCaptureBarrier> Barriers => _barriers;

    /// <summary>
    /// Makes the session wait for a barrier crossing. It does nothing while the session is activated.
    /// </summary>
    public void Enable() => _status = _status == Status.Activated ? _status : Status.Enabled;

    /// <summary>
    /// Moves an enabled session to activated with a new activation id, starts EIS emulation and tells the front.
    /// </summary>
    /// <param name="x">The layout x of the pointer.</param>
    /// <param name="y">The layout y of the pointer.</param>
    /// <param name="barrierId">The id of the crossed barrier.</param>
    /// <returns><see langword="false"/> when the session is not enabled.</returns>
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

    /// <summary>
    /// Gives the input back to the compositor and returns the session to enabled. It does nothing when the session is not activated.
    /// </summary>
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

    /// <summary>
    /// Deactivates the session when needed, then returns it to the created state and tells the front. The front is not told when the session was not enabled.
    /// </summary>
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

    /// <summary>
    /// Handles a client release request. It deactivates the session and raises <see cref="InputCaptureEngine.WarpRequested"/> when a position is given.
    /// </summary>
    /// <param name="activationId">The activation that the client releases.</param>
    /// <param name="x">The layout x to move the pointer to, or -1 for no move.</param>
    /// <param name="y">The layout y to move the pointer to, or -1 for no move.</param>
    /// <returns><see langword="false"/> when the session is activated and <paramref name="activationId"/> is not the current activation.</returns>
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

    /// <summary>
    /// Removes every barrier from the session.
    /// </summary>
    public void ClearBarriers() => _barriers.Clear();

    /// <summary>
    /// Tells whether the session holds a barrier with this id.
    /// </summary>
    /// <param name="id">The barrier id to find.</param>
    /// <returns><see langword="true"/> when a barrier has the id.</returns>
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

    /// <summary>
    /// Adds a barrier when its id is new and <see cref="InputCaptureBarriers.IsValid"/> accepts it. An invalid barrier is kept when <see cref="InputCaptureEngine.EnforceBarriers"/> is off.
    /// </summary>
    /// <param name="barrier">The barrier to add.</param>
    /// <returns><see langword="false"/> when the id exists or the barrier is invalid and enforced.</returns>
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

    /// <summary>
    /// Sends one relative motion to the client.
    /// </summary>
    /// <param name="dx">The relative x motion.</param>
    /// <param name="dy">The relative y motion.</param>
    public void Motion(double dx, double dy) => Eis.SendMotion(dx, dy);

    /// <summary>
    /// Sends one pointer button change to the client.
    /// </summary>
    /// <param name="button">The evdev button code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    public void Button(uint button, bool pressed) => Eis.SendButton(button, pressed);

    /// <summary>
    /// Sends one scroll event to the client.
    /// </summary>
    /// <param name="axis">The scroll axis, value and source.</param>
    public void Axis(in PointerAxis axis) => Eis.SendScroll(in axis);

    /// <summary>
    /// Sends one key change to the client. A press of a repeating key starts the session's own key repeat at the seat's rate.
    /// </summary>
    /// <param name="key">The evdev key code.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
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

    /// <summary>
    /// Sends the xkb modifier state to the client.
    /// </summary>
    /// <param name="depressed">The depressed modifier mask.</param>
    /// <param name="latched">The latched modifier mask.</param>
    /// <param name="locked">The locked modifier mask.</param>
    /// <param name="group">The effective layout group.</param>
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

    /// <summary>
    /// Deactivates the session, disposes its EIS sender and removes it from the engine.
    /// </summary>
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
