using Pixman;

namespace Basin.Capabilities;

public struct UIFrame : IDisposable
{
    private BufferLock _lock;

    public UIFrame(BufferLock bufferLock, PixmanRegion32? damage)
    {
        _lock = bufferLock;
        Damage = damage;
    }

    public readonly IBuffer? Buffer => _lock.Buffer;

    public PixmanRegion32? Damage { get; }

    public void Dispose() => _lock.Dispose();
}
