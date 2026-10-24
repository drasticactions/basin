using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

/// <summary>
/// The reply to one call: a result, or an error, and the descriptors that go with it. A front reuses one reply for each call.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcReply"/> class.
    /// </summary>
    /// <param name="sink">The sink that takes the finished reply.</param>
    public IpcReply(IIpcReplySink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Sink = sink;
    }

    /// <summary>
    /// Gets the sink that takes the finished reply.
    /// </summary>
    public IIpcReplySink Sink { get; }

    /// <summary>
    /// Gets the name of the called method.
    /// </summary>
    public string Method { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the raw JSON bytes of the request id. It is empty when the request has no id.
    /// </summary>
    public ReadOnlySpan<byte> Id => _id.AsSpan(0, _idLength);

    /// <summary>
    /// Gets or sets a value that the front attaches to the call, such as the line formatter of the line front.
    /// </summary>
    public object? FrontState { get; set; }

    /// <summary>
    /// Gets a value indicating whether the handler deferred the reply through <see cref="Defer"/>.
    /// </summary>
    public bool IsDeferred { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the reply carries an error.
    /// </summary>
    public bool IsError => ErrorCode is not null;

    /// <summary>
    /// Gets the error code, or <see langword="null"/> when the reply has no error.
    /// </summary>
    public string? ErrorCode { get; private set; }

    /// <summary>
    /// Gets the error text, or <see langword="null"/> when the reply has no error.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the state of the client that made the call.
    /// </summary>
    public IpcClientState State => Sink.State;

    /// <summary>
    /// Gets the writer of the result JSON. The writer is made on first use. A handler that writes to it must leave it balanced, or the call answers <c>internal</c>.
    /// </summary>
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

    /// <summary>
    /// Gets the result JSON written so far. It is <c>{}</c> when nothing was written.
    /// </summary>
    public ReadOnlySpan<byte> ResultJson
    {
        get
        {
            _writer?.Flush();
            return _result.Length == 0 ? "{}"u8 : _result.Written;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the handler wrote any result JSON.
    /// </summary>
    public bool HasResult
    {
        get
        {
            _writer?.Flush();
            return _result.Length > 0;
        }
    }

    /// <summary>
    /// Gets the number of attached descriptors.
    /// </summary>
    public int FdCount => _fds.Count;

    /// <summary>
    /// Gets the attached descriptors in index order. The reply still owns them.
    /// </summary>
    public ReadOnlySpan<int> Fds => CollectionsMarshal.AsSpan(_fds);

    /// <summary>
    /// Resets the reply for a new call. It closes descriptors left from the last call and clears the result, the error and the deferral.
    /// </summary>
    /// <param name="id">The raw JSON bytes of the request id.</param>
    /// <param name="method">The method name.</param>
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

    /// <summary>
    /// Serializes a result contract into <see cref="Result"/>.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="value">The result.</param>
    /// <param name="info">The source-generated metadata of <typeparamref name="T"/>.</param>
    public void Write<T>(T value, JsonTypeInfo<T> info)
    {
        ArgumentNullException.ThrowIfNull(info);
        JsonSerializer.Serialize(Result, value, info);
    }

    /// <summary>
    /// Replaces the result with an error. The first error wins, and a later call does nothing. The attached descriptors are closed.
    /// </summary>
    /// <param name="code">The error code, for example one of <see cref="IpcErrorCodes"/>.</param>
    /// <param name="message">The error text.</param>
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

    /// <summary>
    /// Attaches a descriptor that the reply sends with its frame. The reply takes ownership and closes it if the reply is not sent.
    /// </summary>
    /// <param name="fd">The descriptor.</param>
    /// <returns>The index that the result JSON uses to name the descriptor.</returns>
    /// <exception cref="InvalidOperationException">The reply already carries the maximum number of descriptors.</exception>
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

    /// <summary>
    /// Defers the reply so that the handler can complete it later. The front then does not send this reply.
    /// </summary>
    /// <returns>The pending reply that the handler must complete.</returns>
    /// <exception cref="InvalidOperationException">The reply was deferred before.</exception>
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

    /// <summary>
    /// Moves the attached descriptors out of the reply. The caller then owns them.
    /// </summary>
    /// <param name="into">The span that takes the descriptors. It must hold at least <see cref="FdCount"/> items.</param>
    /// <returns>The number of descriptors moved.</returns>
    public int TakeFds(Span<int> into)
    {
        var count = _fds.Count;
        CollectionsMarshal.AsSpan(_fds).CopyTo(into);
        _fds.Clear();
        return count;
    }

    /// <summary>
    /// Closes every attached descriptor.
    /// </summary>
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
