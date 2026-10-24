namespace Basin.Ipc;

/// <summary>
/// Describes the call that an <see cref="IIpcInterceptor"/> callback sees. The server reuses each instance, so it is valid only for the length of one callback.
/// </summary>
public sealed unsafe class IpcCallContext
{
    private readonly IpcServer _server;
    private IpcReply? _reply;
    private byte* _parameters;
    private int _length;
    private IpcHeldCall? _held;

    internal IpcCallContext(IpcServer server) => _server = server;

    /// <summary>
    /// Gets the state of the connection or line front that made the call.
    /// </summary>
    public IpcClientState Client { get; private set; } = null!;

    /// <summary>
    /// Gets a value indicating whether the call came from the stdin line front instead of a socket connection.
    /// </summary>
    public bool FromLineFront { get; private set; }

    /// <summary>
    /// Gets the name of the called method.
    /// </summary>
    public string Method { get; private set; } = string.Empty;

    internal long Started { get; set; }

    internal IpcHeldCall? Held => _held;

    /// <summary>
    /// Holds the call so that the interceptor can answer it later. The held call keeps a copy of the parameters. A second call returns the same instance.
    /// </summary>
    /// <returns>The held call, which the interceptor must allow or deny once.</returns>
    /// <exception cref="InvalidOperationException">The context is not inside <see cref="IIpcInterceptor.Before"/>.</exception>
    public IpcHeldCall Hold()
    {
        if (_reply is null)
        {
            throw new InvalidOperationException("The call is not in Before. Hold a call from inside Before only.");
        }

        return _held ??= new IpcHeldCall(_server, Method, new ReadOnlySpan<byte>(_parameters, _length), Client, FromLineFront, Started);
    }

    internal void Begin(IpcReply reply, string method, byte* parameters, int length, long started)
    {
        _reply = reply;
        _held = null;
        Method = method;
        Client = reply.State;
        FromLineFront = reply.Sink is IpcLineFront;
        Started = started;
        _parameters = parameters;
        _length = length;
    }

    internal void BeginAfter(string method, IpcClientState client, bool fromLineFront)
    {
        _reply = null;
        _held = null;
        _parameters = null;
        _length = 0;
        Method = method;
        Client = client;
        FromLineFront = fromLineFront;
    }

    internal void End()
    {
        _reply = null;
        _held = null;
        _parameters = null;
        _length = 0;
    }
}
