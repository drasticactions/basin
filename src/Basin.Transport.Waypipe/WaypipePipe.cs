namespace Basin.Transport.Waypipe;

/// <summary>
/// One pipe that a waypipe channel carries, for example for a clipboard transfer. It buffers inbound bytes and sends outbound bytes as transfer messages.
/// </summary>
public sealed class WaypipePipe : Basin.IPipeToClient
{
    private const int MaxTransferBytes = 64 * 1024;

    private readonly List<byte> _buffered = [];
    private readonly int _maxBytes;
    private Basin.IPipeFromClient? _forward;

    internal WaypipePipe(int remoteId, WaypipeMessageType kind, int maxBytes)
    {
        RemoteId = remoteId;
        Kind = kind;
        _maxBytes = maxBytes;
    }

    /// <summary>
    /// Gets the remote id of the pipe on the channel. Pipes that the compositor opens have negative ids.
    /// </summary>
    public int RemoteId { get; }

    /// <summary>
    /// Gets the open message that created the pipe, which tells its direction.
    /// </summary>
    public WaypipeMessageType Kind { get; }

    /// <summary>
    /// Gets a value indicating whether the write direction is closed, by <see cref="CloseWrite"/> or by the peer.
    /// </summary>
    public bool WriteClosed { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the peer closed the read direction.
    /// </summary>
    public bool ReadClosed { get; private set; }

    /// <summary>
    /// Gets a value indicating whether both directions are closed.
    /// </summary>
    public bool IsFinished => WriteClosed && ReadClosed;

    /// <summary>
    /// Gets the number of buffered inbound bytes that <see cref="Take"/> has not taken.
    /// </summary>
    public int Available => _buffered.Count;

    /// <summary>
    /// Occurs on the reader side after inbound bytes are added to the buffer. It does not occur when the pipe forwards its bytes.
    /// </summary>
    public event Action<WaypipePipe>? Received;

    /// <summary>
    /// Gets a value indicating whether the compositor can write to this pipe.
    /// </summary>
    public bool CanWrite => _writer is not null;

    /// <summary>
    /// Sends bytes to the peer in transfers of at most 64 KiB. It does nothing when a direction is closed.
    /// </summary>
    /// <param name="bytes">The bytes to send.</param>
    /// <exception cref="InvalidOperationException">The pipe carries bytes only toward the compositor.</exception>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (_writer is not { } writer)
        {
            throw new InvalidOperationException($"Pipe {RemoteId} carries bytes toward the compositor. The compositor cannot write to it.");
        }

        if (bytes.IsEmpty || WriteClosed || ReadClosed)
        {
            return;
        }

        while (bytes.Length > MaxTransferBytes)
        {
            writer(this, bytes[..MaxTransferBytes].ToArray());
            bytes = bytes[MaxTransferBytes..];
        }

        writer(this, bytes.ToArray());
    }

    /// <summary>
    /// Closes the write direction and tells the peer. It does nothing for a pipe that the compositor cannot write, or when the direction is already closed.
    /// </summary>
    public void CloseWrite()
    {
        if (_closer is not { } closer || WriteClosed)
        {
            return;
        }

        WriteClosed = true;
        closer(this);
    }

    internal void Attach(Action<WaypipePipe, byte[]> writer, Action<WaypipePipe> closer)
    {
        _writer = writer;
        _closer = closer;
    }

    internal void ForwardTo(Basin.IPipeFromClient sink) => _forward = sink;

    internal void FailForward() => _forward?.Complete();

    private Action<WaypipePipe, byte[]>? _writer;
    private Action<WaypipePipe>? _closer;

    /// <summary>
    /// Takes all buffered inbound bytes.
    /// </summary>
    /// <returns>A new array with the bytes, which can be empty.</returns>
    public byte[] Take()
    {
        var bytes = _buffered.ToArray();
        _buffered.Clear();
        return bytes;
    }

    internal void Receive(ReadOnlySpan<byte> payload)
    {
        if (_forward is { } forward)
        {
            forward.Deliver(payload);
            return;
        }

        if (_buffered.Count + payload.Length > _maxBytes)
        {
            throw new WaypipeException(
                $"Pipe {RemoteId} holds {_buffered.Count} bytes, and {payload.Length} more bytes are over its budget.");
        }

        _buffered.AddRange(payload);
        Received?.Invoke(this);
    }

    internal void Shutdown(WaypipeMessageType type)
    {
        if (type == WaypipeMessageType.PipeShutdownW)
        {
            WriteClosed = true;
            _forward?.Complete();
        }
        else
        {
            ReadClosed = true;
        }
    }
}
