using Pixman;

namespace Basin.Capabilities;

public struct UIFrame : IDisposable
{
    private BufferLock _lock;
    private int _acquireFencePlusOne;

    public UIFrame(BufferLock bufferLock, PixmanRegion32? damage)
        : this(bufferLock, damage, acquireFenceFd: -1)
    {
    }

    public UIFrame(BufferLock bufferLock, PixmanRegion32? damage, int acquireFenceFd)
    {
        _lock = bufferLock;
        Damage = damage;
        _acquireFencePlusOne = acquireFenceFd < 0 ? 0 : acquireFenceFd + 1;
    }

    public readonly IBuffer? Buffer => _lock.Buffer;

    public PixmanRegion32? Damage { get; }

    public readonly int AcquireFenceFd => _acquireFencePlusOne - 1;

    public void Dispose()
    {
        RenderFences.CloseFence(_acquireFencePlusOne - 1);
        _acquireFencePlusOne = 0;
        _lock.Dispose();
    }
}
