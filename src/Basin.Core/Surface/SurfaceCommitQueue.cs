namespace Basin;

internal sealed class SurfaceCommitQueue : IDisposable
{
    private readonly List<ParkedCommit> _parked = [];
    private readonly Stack<SurfaceState> _free = new();

    public bool IsEmpty => _parked.Count == 0;

    public long EarliestTargetTimeNanos
    {
        get
        {
            var earliest = 0L;
            foreach (var parked in _parked)
            {
                if ((parked.Reason & CommitParkReason.CommitTiming) != 0 &&
                    parked.TargetTimeNanos > 0 &&
                    (earliest == 0 || parked.TargetTimeNanos < earliest))
                {
                    earliest = parked.TargetTimeNanos;
                }
            }

            return earliest;
        }
    }

    public void Park(SurfaceState pending, CommitParkReason reason, long targetTimeNanos, bool armsBarrier)
    {
        var state = _free.Count > 0 ? _free.Pop() : new SurfaceState();
        SurfaceCommit.Move(pending, state);
        _parked.Add(new ParkedCommit(state, reason, targetTimeNanos, armsBarrier));
    }

    public bool TryReleaseReady(
        long nowNanos, bool barrierCleared, bool holdCleared, out SurfaceState state, out bool armsBarrier)
    {
        if (_parked.Count > 0 && IsReady(_parked[0], nowNanos, barrierCleared, holdCleared))
        {
            var head = _parked[0];
            _parked.RemoveAt(0);
            state = head.State;
            armsBarrier = head.ArmsBarrier;
            return true;
        }

        state = null!;
        armsBarrier = false;
        return false;
    }

    public void Recycle(SurfaceState state) => _free.Push(state);

    public void Dispose()
    {
        foreach (var parked in _parked)
        {
            parked.State.Dispose();
        }

        _parked.Clear();

        while (_free.Count > 0)
        {
            _free.Pop().Dispose();
        }
    }

    private static bool IsReady(in ParkedCommit parked, long nowNanos, bool barrierCleared, bool holdCleared)
    {
        if ((parked.Reason & CommitParkReason.FifoBarrier) != 0 && !barrierCleared)
        {
            return false;
        }

        if ((parked.Reason & CommitParkReason.Held) != 0 && !holdCleared)
        {
            return false;
        }

        return (parked.Reason & CommitParkReason.CommitTiming) == 0 ||
               parked.TargetTimeNanos <= 0 ||
               nowNanos >= parked.TargetTimeNanos;
    }

    private readonly struct ParkedCommit(
        SurfaceState state,
        CommitParkReason reason,
        long targetTimeNanos,
        bool armsBarrier)
    {
        public SurfaceState State { get; } = state;

        public CommitParkReason Reason { get; } = reason;

        public long TargetTimeNanos { get; } = targetTimeNanos;

        public bool ArmsBarrier { get; } = armsBarrier;
    }
}
