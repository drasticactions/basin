namespace TabWm;

internal sealed class TabCloser<T>
    where T : class
{
    private readonly List<T> _remaining;
    private readonly long _deadline;

    public TabCloser(IEnumerable<T> windows, long nowMs, int timeoutMs, Action<T> close)
    {
        _remaining = [.. windows];
        _deadline = nowMs + timeoutMs;
        foreach (var window in _remaining.ToArray())
        {
            close(window);
        }

        State = _remaining.Count == 0 ? TabCloseState.Done : TabCloseState.Waiting;
    }

    public TabCloseState State { get; private set; }

    public IReadOnlyList<T> Remaining => _remaining;

    public long Deadline => _deadline;

    public void Gone(T window)
    {
        if (!_remaining.Remove(window) || State is TabCloseState.Cancelled)
        {
            return;
        }

        if (_remaining.Count == 0)
        {
            State = TabCloseState.Done;
        }
    }

    public TabCloseState Tick(long nowMs)
    {
        if (State == TabCloseState.Waiting && nowMs >= _deadline)
        {
            State = TabCloseState.Refused;
        }

        return State;
    }

    public void Cancel()
    {
        if (State is TabCloseState.Waiting or TabCloseState.Refused)
        {
            State = TabCloseState.Cancelled;
        }
    }
}
