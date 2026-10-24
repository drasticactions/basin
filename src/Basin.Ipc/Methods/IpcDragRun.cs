using Basin.Capabilities;

namespace Basin.Ipc;

internal sealed class IpcDragRun : IFrameSink, IDisposable
{
    private const uint LeftButton = 0x110;

    private readonly IpcServer _server;
    private readonly ISyntheticInput _input;
    private readonly IFrameClock? _clock;
    private readonly IToplevelModel? _model;
    private readonly IpcPendingReply _reply;
    private readonly ulong _window;
    private readonly string _part;
    private readonly double _fromX;
    private readonly double _fromY;
    private readonly double _dx;
    private readonly double _dy;
    private readonly int _steps;
    private readonly int _intervalMs;
    private readonly int _settleMs;
    private IEventSource? _fallback;
    private IpcInputSettle? _settle;
    private long _lastVblank = -1;
    private int _step;
    private int _frames;
    private bool _pressed;
    private bool _released;
    private bool _done;

    public IpcDragRun(
        IpcServer server, ISyntheticInput input, IFrameClock? clock, IToplevelModel? model, IpcPendingReply reply, ulong window, string part,
        double fromX, double fromY, double dx, double dy, int steps, int intervalMs, int settleMs)
    {
        _server = server;
        _input = input;
        _clock = clock;
        _model = model;
        _reply = reply;
        _window = window;
        _part = part;
        _fromX = fromX;
        _fromY = fromY;
        _dx = dx;
        _dy = dy;
        _steps = steps;
        _intervalMs = intervalMs;
        _settleMs = settleMs;
    }

    private static uint Now => (uint)Environment.TickCount;

    public void Start()
    {
        _server.Own(this);
        if (!_input.PointerMotionAbsolute(Now, _fromX, _fromY))
        {
            Fail("The pointer did not move to the start point.");
            return;
        }

        _fallback = _server.Loop.AddTimer(Step);
        _clock?.Add(this);
        Arm();
    }

    public void EndFrame(IOutput output, long presentedNanos)
    {
    }

    public void BeginFrame(IOutput output, long predictedVblankNanos)
    {
        if (_done || _released || predictedVblankNanos == _lastVblank)
        {
            return;
        }

        _lastVblank = predictedVblankNanos;
        _frames++;
        Step();
    }

    public void Dispose()
    {
        if (_done)
        {
            return;
        }

        Stop();
        _settle?.Dispose();
        if (!_reply.IsDone)
        {
            _reply.Error(IpcErrorCodes.Failed, "The compositor stopped before the drag ended.");
            _ = _reply.Complete();
        }
    }

    private void Arm()
    {
        _fallback?.UpdateTimer(_intervalMs);
        _clock?.RequestFrame();
    }

    private void Step()
    {
        if (_done || _released)
        {
            return;
        }

        if (!_pressed)
        {
            if (!_input.PointerButton(Now, LeftButton, true))
            {
                Fail("The compositor refused the button press.");
                return;
            }

            _pressed = true;
            Arm();
            return;
        }

        if (_step < _steps)
        {
            _step++;
            var t = (double)_step / _steps;
            _ = _input.PointerMotionAbsolute(Now, _fromX + (_dx * t), _fromY + (_dy * t));
            Arm();
            return;
        }

        _ = _input.PointerButton(Now, LeftButton, false);
        _pressed = false;
        _released = true;
        _clock?.Remove(this);
        _fallback?.Remove();
        _fallback = null;
        _settle = new IpcInputSettle(_server, _clock, _model, _window, quietMs: 100, _settleMs, (settled, waitedMs) =>
        {
            _settle = null;
            if (!_reply.IsDone)
            {
                _reply.Write(
                    new IpcDiagDrag(_window, _part, _fromX, _fromY, _fromX + _dx, _fromY + _dy, _steps, settled, _frames),
                    IpcJsonContext.Default.IpcDiagDrag);
                _ = _reply.Complete();
            }

            Stop();
        });
        _settle.Start();
    }

    private void Stop()
    {
        if (_done)
        {
            return;
        }

        _done = true;
        _clock?.Remove(this);
        _fallback?.Remove();
        _fallback = null;
        _server.Disown(this);
        if (_pressed)
        {
            _ = _input.PointerButton(Now, LeftButton, false);
            _pressed = false;
        }
    }

    private void Fail(string message)
    {
        _reply.Error(IpcErrorCodes.Refused, message);
        _ = _reply.Complete();
        Stop();
    }
}
