namespace Basin;

public sealed class PipeFromClient : IPipeFromClient
{
    private readonly object _lock = new();
    private readonly MemoryStream _buffer = new();
    private readonly int _maxBytes;
    private bool _complete;
    private bool _overflowed;

    public PipeFromClient(int maxBytes = 16 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        _maxBytes = maxBytes;
    }

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

    public void Complete()
    {
        lock (_lock)
        {
            _complete = true;
            Monitor.PulseAll(_lock);
        }
    }

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

                _ = Monitor.Wait(_lock, (int)Math.Min(remaining, int.MaxValue));
            }

            return _overflowed ? null : _buffer.ToArray();
        }
    }
}
