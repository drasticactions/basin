namespace Basin;

/// <summary>
/// One lock on an <see cref="IBuffer"/>, returned by <see cref="IBuffer.Lock"/>. Disposing it calls <see cref="IBuffer.Unlock"/> once.
/// </summary>
public struct BufferLock : IDisposable
{
    private IBuffer? _buffer;

    internal BufferLock(IBuffer buffer) => _buffer = buffer;

    /// <summary>
    /// Gets the locked buffer. It is <see langword="null"/> after <see cref="Dispose"/> and on a default instance.
    /// </summary>
    public readonly IBuffer? Buffer => _buffer;

    /// <summary>
    /// Releases the lock. A second call, or a call on a default instance, does nothing.
    /// </summary>
    public void Dispose()
    {
        _buffer?.Unlock();
        _buffer = null;
    }
}
