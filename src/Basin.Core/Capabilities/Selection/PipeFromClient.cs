namespace Basin;

/// <summary>
/// Collects the bytes that a client writes into a managed pipe, so that another thread can wait for all of them. It is thread-safe.
/// </summary>
public sealed class PipeFromClient : IPipeFromClient
{
    private readonly object _lock = new();
    private readonly MemoryStream _buffer = new();
    private readonly int _maxBytes;
    private bool _complete;
    private bool _overflowed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipeFromClient"/> class.
    /// </summary>
    /// <param name="maxBytes">The largest number of bytes to accept. More bytes end the transfer as a failure. It must be positive.</param>
    public PipeFromClient(int maxBytes = 16 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        _maxBytes = maxBytes;
    }

    /// <summary>
    /// Appends bytes. Bytes after completion are ignored, and bytes past the limit end the transfer as a failure.
    /// </summary>
    /// <param name="bytes">The bytes that the client wrote.</param>
    public void Deliver(ReadOnlySpan<byte> bytes)
    {
        lock (_lock)
        {
            if (_complete)
            {
                return;
            }

            if (_buffer.Length + bytes.Length > _maxBytes)
            {
                _overflowed = true;
                _complete = true;
                Monitor.PulseAll(_lock);
                return;
            }

            _buffer.Write(bytes);
        }
    }

    /// <summary>
    /// Marks the transfer as complete and wakes a waiting reader.
    /// </summary>
    public void Complete()
    {
        lock (_lock)
        {
            _complete = true;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>
    /// Waits until the client closes its end, then returns all the bytes.
    /// </summary>
    /// <param name="timeout">The longest time to wait.</param>
    /// <returns>The bytes, or <see langword="null"/> when the timeout ends first, the data went past the limit, or the platform has no threads to wait on.</returns>
    public byte[]? ReadToEnd(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        lock (_lock)
        {
            while (!_complete)
            {
                var remaining = deadline - Environment.TickCount64;
                if (remaining <= 0)
                {
                    return null;
                }

                if (!PlatformFacts.HasThreads)
                {
                    return null;
                }

                _ = Monitor.Wait(_lock, (int)Math.Min(remaining, int.MaxValue));
            }

            return _overflowed ? null : _buffer.ToArray();
        }
    }
}
