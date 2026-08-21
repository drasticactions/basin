namespace Basin;

public struct BufferLock : IDisposable
{
    private IBuffer? _buffer;

    internal BufferLock(IBuffer buffer) => _buffer = buffer;

    public readonly IBuffer? Buffer => _buffer;

    public void Dispose()
    {
        _buffer?.Unlock();
        _buffer = null;
    }
}
