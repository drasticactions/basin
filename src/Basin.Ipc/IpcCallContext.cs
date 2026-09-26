namespace Basin.Ipc;

public sealed unsafe class IpcCallContext
{
    private readonly IpcServer _server;
    private IpcReply? _reply;
    private byte* _parameters;
    private int _length;
    private IpcHeldCall? _held;

    internal IpcCallContext(IpcServer server) => _server = server;

    public IpcClientState Client { get; private set; } = null!;

    public bool FromLineFront { get; private set; }

    public string Method { get; private set; } = string.Empty;

    internal long Started { get; set; }

    internal IpcHeldCall? Held => _held;

    public IpcHeldCall Hold()
    {
        if (_reply is null)
        {
            throw new InvalidOperationException("a call is held from inside Before");
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
