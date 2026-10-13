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

    public void Park(SurfaceState pending, CommitParkReason reason, long targetTimeNanos, bool armsBarrier, object? fence = null)
    {
        var state = _free.Count > 0 ? _free.Pop() : new SurfaceState();
        SurfaceCommit.Move(pending, state);
        _parked.Add(new ParkedCommit(state, reason, targetTimeNanos, armsBarrier, fence));
    }

    public bool ClearFence(object fence)
    {
        for (var i = 0; i < _parked.Count; i++)
        {
            if (ReferenceEquals(_parked[i].Fence, fence))
            {
                var parked = _parked[i];
                _parked[i] = new ParkedCommit(
                    parked.State, parked.Reason & ~CommitParkReason.Fence, parked.TargetTimeNanos, parked.ArmsBarrier, null);
                return true;
            }
        }

        return false;
    }

    public bool TryReleaseReady(
        long nowNanos, bool barrierCleared, bool holdCleared, out SurfaceState state, out bool armsBarrier) =>
        TryReleaseReady(nowNanos, barrierCleared, holdCleared, 0, 0, -1, out state, out armsBarrier);

    public bool TryReleaseReady(
        long nowNanos, bool barrierCleared, bool holdCleared, long anchor, long anchorGrid, long grid,
        out SurfaceState state, out bool armsBarrier)
    {
        if (_parked.Count > 0 && IsReady(_parked[0], nowNanos, barrierCleared, holdCleared, anchor, anchorGrid, grid))
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

    private static bool IsReady(
        in ParkedCommit parked, long nowNanos, bool barrierCleared, bool holdCleared, long anchor, long anchorGrid, long grid)
    {
        if ((parked.Reason & CommitParkReason.FifoBarrier) != 0 && !barrierCleared)
        {
            return false;
        }

        if ((parked.Reason & CommitParkReason.Held) != 0 && !holdCleared)
        {
            return false;
        }

        if ((parked.Reason & CommitParkReason.Fence) != 0)
        {
            return false;
        }

        return (parked.Reason & CommitParkReason.CommitTiming) == 0 ||
               parked.TargetTimeNanos <= 0 ||
               (grid < 0
                   ? nowNanos >= parked.TargetTimeNanos
                   : PresentTiming.TargetReached(parked.TargetTimeNanos, nowNanos, anchor, anchorGrid, grid));
    }

    private readonly struct ParkedCommit(
        SurfaceState state,
        CommitParkReason reason,
        long targetTimeNanos,
        bool armsBarrier,
        object? fence)
    {
        public SurfaceState State { get; } = state;

        public CommitParkReason Reason { get; } = reason;

        public long TargetTimeNanos { get; } = targetTimeNanos;

        public bool ArmsBarrier { get; } = armsBarrier;

        public object? Fence { get; } = fence;
    }
}
