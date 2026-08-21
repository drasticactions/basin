using Wayland.Server;

namespace Basin.Transport.Waypipe;

public sealed class WaypipeClientTransport : IWlClientTransport
{
    private readonly object _lock = new();
    private readonly Queue<byte> _inbound = new();
    private readonly Queue<int> _inboundSlots = new();
    private readonly FdSlotTable _slots = new();
    private WlTransportSignal? _signal;
    private bool _readShutdown;
    private bool _disposed;

    public event WaypipeOutboundHandler? Outbound;

    public IFdSlotTable? FdSlots => _slots;

    public FdSlotTable Slots => _slots;

    public int? PollFd => null;

    public bool IsReadBroken => _readShutdown;

    public void Deliver(ReadOnlySpan<byte> bytes, ReadOnlySpan<int> fdSlots)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var slot in fdSlots)
            {
                _inboundSlots.Enqueue(slot);
            }

            foreach (var value in bytes)
            {
                _inbound.Enqueue(value);
            }
        }

        _signal?.NotifyReadable();
    }

    public void EndOfStream()
    {
        lock (_lock)
        {
            _readShutdown = true;
        }

        _signal?.NotifyReadable();
    }

    public (int BytesRead, int FdsRead) TryReadNonBlocking(
        Memory<byte> buffer1, Memory<byte> buffer2, Memory<int> fdBuf1, Memory<int> fdBuf2)
    {
        lock (_lock)
        {
            var fds = 0;
            fds += Drain(_inboundSlots, fdBuf1.Span, fdBuf2.Span);

            var bytes = 0;
            bytes += Drain(_inbound, buffer1.Span, buffer2.Span);

            if (bytes == 0 && fds == 0)
            {
                return _readShutdown ? (0, 0) : (-1, 0);
            }

            return (bytes, fds);
        }
    }

    public int TryWriteNonBlocking(ReadOnlyMemory<byte> buffer, ReadOnlyMemory<int> fds)
    {
        if (_disposed)
        {
            return buffer.Length;
        }

        Outbound?.Invoke(buffer.Span, fds.Span);
        return buffer.Length;
    }

    public void ShutdownRead() => EndOfStream();

    public void CloseFd(int fd) => _slots.Close(fd);

    public int DuplicateFd(int fd) => _slots.Duplicate(fd);

    public void SetSignal(WlTransportSignal signal)
    {
        _signal = signal;
        lock (_lock)
        {
            if (_inbound.Count > 0 || _inboundSlots.Count > 0 || _readShutdown)
            {
                signal.NotifyReadable();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _inbound.Clear();
            while (_inboundSlots.Count > 0)
            {
                _slots.Close(_inboundSlots.Dequeue());
            }
        }
    }

    private static int Drain<T>(Queue<T> queue, Span<T> first, Span<T> second)
    {
        var taken = 0;
        while (queue.Count > 0 && taken < first.Length + second.Length)
        {
            var value = queue.Dequeue();
            if (taken < first.Length)
            {
                first[taken] = value;
            }
            else
            {
                second[taken - first.Length] = value;
            }

            taken++;
        }

        return taken;
    }
}
