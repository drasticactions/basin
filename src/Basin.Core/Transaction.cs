using Basin.Diagnostics;

namespace Basin;

public sealed class Transaction : IDisposable
{
    public const int DefaultTimeoutMs = 100;

    private readonly ICompositorEventLoop _loop;
    private readonly int _timeoutMs;
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();

    private readonly List<bool> _ready = [];
    private IEventSource? _deadline;
    private int _outstanding;
    private int _joined;
    private bool _sealed;
    private bool _completing;
    private bool _disposed;

    public Transaction(ICompositorEventLoop loop, int timeoutMs = DefaultTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        _loop = loop;
        _timeoutMs = timeoutMs;
        BasinCounters.Track();
    }

    public static long TimedOutCount { get; private set; }

    public static void ResetCounters() => TimedOutCount = 0;

    public bool IsComplete { get; private set; }

    public bool TimedOut { get; private set; }

    public bool IsSealed => _sealed;

    public int Outstanding => _outstanding;

    public int Participants => _joined;

    public event Action? Completed;

    public TransactionParticipant Join()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed)
        {
            throw new InvalidOperationException("participants cannot join a sealed transaction");
        }

        _outstanding++;
        _ready.Add(false);
        return new TransactionParticipant(this, _joined++);
    }

    public void Seal()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed)
        {
            return;
        }

        _sealed = true;
        if (_outstanding == 0)
        {
            QueueCompletion();
            return;
        }

        _deadline = _loop.AddTimer(OnDeadline);
        _deadline.UpdateTimer(_timeoutMs);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _deadline?.Remove();
        _deadline = null;
        _ready.Clear();
        Completed = null;
        BasinCounters.Untrack();
    }

    internal void ReportReady(int index)
    {
        _thread.Assert();
        if (IsComplete || _disposed || index < 0 || index >= _ready.Count || _ready[index])
        {
            return;
        }

        _ready[index] = true;
        _outstanding--;
        if (_outstanding == 0 && _sealed)
        {
            QueueCompletion();
        }
    }

    private void OnDeadline()
    {
        if (IsComplete || _disposed)
        {
            return;
        }

        TimedOut = true;
        TimedOutCount++;
        QueueCompletion();
    }

    private void QueueCompletion()
    {
        if (_completing || IsComplete)
        {
            return;
        }

        _completing = true;
        _deadline?.Remove();
        _deadline = null;
        _loop.AddIdle(() =>
        {
            if (IsComplete || _disposed)
            {
                return;
            }

            IsComplete = true;
            Completed?.Invoke();
        });
    }
}
