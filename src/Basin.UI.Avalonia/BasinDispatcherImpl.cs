using System.Diagnostics;
using Avalonia.Threading;

namespace Basin.UI.Avalonia;

internal sealed class BasinDispatcherImpl : IDispatcherImpl
{
    private const int MaxPumpRounds = 64;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private volatile bool _signalPending;
    private long? _timerDueMillis;

    public event Action? Signaled;

    public event Action? Timer;

    public event Action? WakeupRequested;

    public bool CurrentThreadIsLoopThread => Environment.CurrentManagedThreadId == _threadId;

    public long Now => _clock.ElapsedMilliseconds;

    public long? NextDueMillis
    {
        get
        {
            if (_signalPending)
            {
                return 0;
            }

            if (_timerDueMillis is not { } due)
            {
                return null;
            }

            return Math.Max(0, due - Now);
        }
    }

    public void Signal()
    {
        if (_signalPending)
        {
            return;
        }

        _signalPending = true;
        WakeupRequested?.Invoke();
    }

    public void UpdateTimer(long? dueTimeInMs)
    {
        _timerDueMillis = dueTimeInMs;
        if (dueTimeInMs is { } due && due <= Now)
        {
            WakeupRequested?.Invoke();
        }
    }

    public bool Pump()
    {
        var did = false;
        for (var round = 0; round < MaxPumpRounds; round++)
        {
            var worked = false;

            if (_signalPending)
            {
                _signalPending = false;
                Signaled?.Invoke();
                worked = true;
            }

            if (_timerDueMillis is { } due && due <= Now)
            {
                _timerDueMillis = null;
                Timer?.Invoke();
                worked = true;
            }

            if (!worked)
            {
                break;
            }

            did = true;
        }

        return did;
    }
}
