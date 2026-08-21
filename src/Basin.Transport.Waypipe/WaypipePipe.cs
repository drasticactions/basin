namespace Basin.Transport.Waypipe;

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

    public int RemoteId { get; }

    public WaypipeMessageType Kind { get; }

    public bool WriteClosed { get; private set; }

    public bool ReadClosed { get; private set; }

    public bool IsFinished => WriteClosed && ReadClosed;

    public int Available => _buffered.Count;

    public event Action<WaypipePipe>? Received;

    public bool CanWrite => _writer is not null;

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (_writer is not { } writer)
        {
            throw new InvalidOperationException($"pipe {RemoteId} carries bytes toward the compositor and is not ours to write");
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
                $"pipe {RemoteId} holds {_buffered.Count} bytes and the channel added {payload.Length}, over its budget");
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
