namespace Basin.Ipc;

public sealed class IpcHeldCall
{
    private readonly IpcServer _server;
    private readonly byte[] _parameters;
    private IpcPendingReply? _reply;

    internal IpcHeldCall(
        IpcServer server, string method, ReadOnlySpan<byte> parameters, IpcClientState client, bool fromLineFront, long started)
    {
        _server = server;
        Method = method;
        _parameters = parameters.ToArray();
        Client = client;
        FromLineFront = fromLineFront;
        Started = started;
    }

    public string Method { get; }

    public ReadOnlySpan<byte> Parameters => _parameters;

    public IpcClientState Client { get; }

    public bool FromLineFront { get; }

    public bool IsDone { get; private set; }

    public bool IsOpen => !IsDone && _reply is not { IsOpen: false };

    internal long Started { get; }

    public void Allow()
    {
        if (Take() is { } reply)
        {
            _server.RunHeld(this, reply, _parameters);
        }
        else
        {
            _early = true;
        }
    }

    public void Deny(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (Take() is { } reply)
        {
            Refuse(reply, message);
        }
        else
        {
            _early = false;
            _earlyMessage = message;
        }
    }

    private bool? _early;
    private string? _earlyMessage;

    internal void Attach(IpcPendingReply reply)
    {
        _reply = reply;
        if (_early is not { } allowed)
        {
            return;
        }

        _server.ForgetHeld(this);
        if (allowed)
        {
            _server.RunHeld(this, reply, _parameters);
        }
        else
        {
            Refuse(reply, _earlyMessage!);
        }
    }

    private static void Refuse(IpcPendingReply reply, string message)
    {
        reply.Error(IpcErrorCodes.Refused, message);
        _ = reply.Complete();
    }

    private IpcPendingReply? Take()
    {
        if (IsDone)
        {
            throw new InvalidOperationException("a held call is answered once");
        }

        IsDone = true;
        if (_reply is not { } reply)
        {
            return null;
        }

        _server.ForgetHeld(this);
        return reply;
    }
}
