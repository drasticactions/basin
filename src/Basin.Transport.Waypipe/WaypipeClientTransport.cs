using Wayland.Server;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The in-memory Wayland transport for one remote client. The engine delivers inbound bytes and descriptor slots, and <see cref="Outbound"/> carries what the compositor writes.
/// </summary>
public sealed class WaypipeClientTransport : IWlClientTransport
{
    private readonly object _lock = new();
    private readonly Queue<byte> _inbound = new();
    private readonly Queue<int> _inboundSlots = new();
    private readonly FdSlotTable _slots = new();
    private WlTransportSignal? _signal;
    private bool _readShutdown;
    private bool _disposed;

    /// <summary>
    /// Occurs on the compositor thread for each write that the compositor makes to the client.
    /// </summary>
    public event WaypipeOutboundHandler? Outbound;

    /// <summary>
    /// Gets the slot table that stands in for descriptors. This value is never <see langword="null"/>.
    /// </summary>
    public IFdSlotTable? FdSlots => _slots;

    /// <summary>
    /// Gets the slot table as its concrete type, so the engine can mint slots for regions and pipes.
    /// </summary>
    public FdSlotTable Slots => _slots;

    /// <summary>
    /// Gets the descriptor to poll. This value is always <see langword="null"/>, because the transport signals readiness through <see cref="SetSignal"/>.
    /// </summary>
    public int? PollFd => null;

    /// <summary>
    /// Gets a value indicating whether the inbound direction has ended.
    /// </summary>
    public bool IsReadBroken => _readShutdown;

    /// <summary>
    /// Queues inbound protocol bytes and descriptor slots, then marks the client readable. It can be called from any thread. After disposal it does nothing.
    /// </summary>
    /// <param name="bytes">The Wayland protocol bytes from the remote client.</param>
    /// <param name="fdSlots">The descriptor slots that the bytes carry. The transport owns them after the call.</param>
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

    /// <summary>
    /// Marks the inbound direction as ended. A read reports the end after the queued data is drained.
    /// </summary>
    public void EndOfStream()
    {
        lock (_lock)
        {
            _readShutdown = true;
        }

        _signal?.NotifyReadable();
    }

    /// <summary>
    /// Takes queued bytes and descriptor slots into the two buffer halves.
    /// </summary>
    /// <param name="buffer1">The first part of the byte buffer.</param>
    /// <param name="buffer2">The second part of the byte buffer.</param>
    /// <param name="fdBuf1">The first part of the slot buffer.</param>
    /// <param name="fdBuf2">The second part of the slot buffer.</param>
    /// <returns>The counts that were read, (0, 0) at the end of the stream, or (-1, 0) when nothing is queued yet.</returns>
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

    /// <summary>
    /// Passes a write to <see cref="Outbound"/>. It always accepts all the bytes.
    /// </summary>
    /// <param name="buffer">The protocol bytes to write.</param>
    /// <param name="fds">The descriptor slots that travel with the bytes.</param>
    /// <returns>The length of <paramref name="buffer"/>.</returns>
    public int TryWriteNonBlocking(ReadOnlyMemory<byte> buffer, ReadOnlyMemory<int> fds)
    {
        if (_disposed)
        {
            return buffer.Length;
        }

        Outbound?.Invoke(buffer.Span, fds.Span);
        return buffer.Length;
    }

    /// <summary>
    /// Ends the inbound direction, as <see cref="EndOfStream"/> does.
    /// </summary>
    public void ShutdownRead() => EndOfStream();

    /// <summary>
    /// Closes a descriptor slot.
    /// </summary>
    /// <param name="fd">The slot to close.</param>
    public void CloseFd(int fd) => _slots.Close(fd);

    /// <summary>
    /// Duplicates a descriptor slot.
    /// </summary>
    /// <param name="fd">The slot to duplicate.</param>
    /// <returns>A new slot that refers to the same object.</returns>
    public int DuplicateFd(int fd) => _slots.Duplicate(fd);

    /// <summary>
    /// Sets the signal that marks the client readable. It fires immediately when data or the end of stream is already queued.
    /// </summary>
    /// <param name="signal">The signal from the display.</param>
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

    /// <summary>
    /// Drops queued bytes and closes queued descriptor slots. Later deliveries do nothing and later writes are discarded.
    /// </summary>
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
