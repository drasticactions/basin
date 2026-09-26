using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

public class IpcReply
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IpcByteBuffer _result = new();
    private readonly List<int> _fds = [];
    private byte[] _id = new byte[16];
    private int _idLength;
    private Utf8JsonWriter? _writer;

    public IpcReply(IIpcReplySink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Sink = sink;
    }

    public IIpcReplySink Sink { get; }

    public string Method { get; private set; } = string.Empty;

    public ReadOnlySpan<byte> Id => _id.AsSpan(0, _idLength);

    public object? FrontState { get; set; }

    public bool IsDeferred { get; private set; }

    public bool IsError => ErrorCode is not null;

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public IpcClientState State => Sink.State;

    public Utf8JsonWriter Result
    {
        get
        {
            if (_writer is null)
            {
                _writer = new Utf8JsonWriter(_result, WriterOptions);
            }

            return _writer;
        }
    }

    public ReadOnlySpan<byte> ResultJson
    {
        get
        {
            _writer?.Flush();
            return _result.Length == 0 ? "{}"u8 : _result.Written;
        }
    }

    public bool HasResult
    {
        get
        {
            _writer?.Flush();
            return _result.Length > 0;
        }
    }

    public int FdCount => _fds.Count;

    public ReadOnlySpan<int> Fds => CollectionsMarshal.AsSpan(_fds);

    public void Begin(ReadOnlySpan<byte> id, string method)
    {
        ArgumentNullException.ThrowIfNull(method);
        ReleaseFds();
        if (_id.Length < id.Length)
        {
            _id = new byte[id.Length];
        }

        id.CopyTo(_id);
        _idLength = id.Length;
        Method = method;
        FrontState = null;
        IsDeferred = false;
        Intercepted = null;
        ErrorCode = null;
        ErrorMessage = null;
        ClearResult();
    }

    public void Write<T>(T value, JsonTypeInfo<T> info)
    {
        ArgumentNullException.ThrowIfNull(info);
        JsonSerializer.Serialize(Result, value, info);
    }

    public void Error(string code, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        if (IsError)
        {
            return;
        }

        ErrorCode = code;
        ErrorMessage = message ?? string.Empty;
        ClearResult();
        ReleaseFds();
    }

    public int AttachFd(int fd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fd);
        if (_fds.Count >= UnixSocket.MaxFds)
        {
            throw new InvalidOperationException($"a reply carries at most {UnixSocket.MaxFds} descriptors");
        }

        _fds.Add(fd);
        return _fds.Count - 1;
    }

    public IpcPendingReply Defer()
    {
        if (IsDeferred || this is IpcPendingReply { Rerun: false })
        {
            throw new InvalidOperationException("a reply is deferred once");
        }

        IsDeferred = true;
        if (this is IpcPendingReply pending)
        {
            pending.Rerun = false;
            return pending;
        }

        var deferred = new IpcPendingReply(Sink, Id, Method, FrontState)
        {
            Intercepted = Intercepted,
            CallStarted = CallStarted,
        };
        Intercepted = null;
        return deferred;
    }

    internal IpcServer? Intercepted { get; set; }

    internal long CallStarted { get; set; }

    public int TakeFds(Span<int> into)
    {
        var count = _fds.Count;
        CollectionsMarshal.AsSpan(_fds).CopyTo(into);
        _fds.Clear();
        return count;
    }

    public void ReleaseFds()
    {
        foreach (var fd in _fds)
        {
            _ = UnixSocket.Close(fd);
        }

        _fds.Clear();
    }

    internal bool ResultIsBalanced => _writer is null || _writer.CurrentDepth == 0;

    internal void Fail(string code, string message)
    {
        ErrorCode = null;
        Error(code, message);
    }

    private void ClearResult()
    {
        _result.Clear();
        _writer?.Reset(_result);
    }
}
