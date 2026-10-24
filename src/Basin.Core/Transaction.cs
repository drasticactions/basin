using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// Waits for a set of participants to become ready, with a deadline, then raises <see cref="Completed"/> once from an idle callback. The transaction is single-threaded.
/// </summary>
public sealed class Transaction : IDisposable
{
    /// <summary>
    /// The default deadline in milliseconds, measured from the call to <see cref="Seal"/>.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="Transaction"/> class.
    /// </summary>
    /// <param name="loop">The loop that runs the deadline timer and the completion callback.</param>
    /// <param name="timeoutMs">The deadline in milliseconds. It must be more than 0.</param>
    public Transaction(ICompositorEventLoop loop, int timeoutMs = DefaultTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        _loop = loop;
        _timeoutMs = timeoutMs;
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the number of transactions in the process that completed because the deadline ended.
    /// </summary>
    public static long TimedOutCount { get; private set; }

    /// <summary>
    /// Sets <see cref="TimedOutCount"/> to 0.
    /// </summary>
    public static void ResetCounters() => TimedOutCount = 0;

    /// <summary>
    /// Gets a value indicating whether <see cref="Completed"/> was raised.
    /// </summary>
    public bool IsComplete { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the deadline ended before all participants were ready.
    /// </summary>
    public bool TimedOut { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Seal"/> was called. A sealed transaction accepts no new participants.
    /// </summary>
    public bool IsSealed => _sealed;

    /// <summary>
    /// Gets the number of participants that are not ready yet.
    /// </summary>
    public int Outstanding => _outstanding;

    /// <summary>
    /// Gets the number of participants that joined.
    /// </summary>
    public int Participants => _joined;

    /// <summary>
    /// Occurs once, from an idle callback, after the transaction is sealed and all participants are ready or the deadline ends. It does not occur after <see cref="Dispose"/>.
    /// </summary>
    public event Action? Completed;

    /// <summary>
    /// Adds a participant that the transaction must wait for.
    /// </summary>
    /// <returns>The handle that the participant uses to report that it is ready.</returns>
    public TransactionParticipant Join()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed)
        {
            throw new InvalidOperationException("The transaction is sealed. Add participants before the transaction is sealed.");
        }

        _outstanding++;
        _ready.Add(false);
        return new TransactionParticipant(this, _joined++);
    }

    /// <summary>
    /// Stops new participants and starts the deadline. With no outstanding participants, completion is queued at once. A second call does nothing.
    /// </summary>
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

    /// <summary>
    /// Stops the deadline and removes the <see cref="Completed"/> handlers. The transaction does not complete after this call.
    /// </summary>
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
