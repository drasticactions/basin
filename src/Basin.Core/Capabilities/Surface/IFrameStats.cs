namespace Basin.Capabilities;

public interface IFrameStats
{
    void Acquire();

    void Release();

    bool TryGet(Surface surface, long nowNanos, out FrameStatsSnapshot snapshot);
}
