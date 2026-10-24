namespace Basin.Ipc;

/// <summary>
/// A call that an interceptor held. It keeps a copy of the parameters and must be allowed or denied once.
/// </summary>
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

    /// <summary>
    /// Gets the name of the held method.
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// Gets the copy of the call's params as raw JSON.
    /// </summary>
    public ReadOnlySpan<byte> Parameters => _parameters;

    /// <summary>
    /// Gets the state of the connection or line front that made the call.
    /// </summary>
    public IpcClientState Client { get; }

    /// <summary>
    /// Gets a value indicating whether the call came from the stdin line front.
    /// </summary>
    public bool FromLineFront { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Allow"/> or <see cref="Deny"/> ran.
    /// </summary>
    public bool IsDone { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the call has no answer and its connection is not closed.
    /// </summary>
    public bool IsOpen => !IsDone && _reply is not { IsOpen: false };

    internal long Started { get; }

    /// <summary>
    /// Runs the held method on the loop. An answer that comes before the server attaches the reply is kept and applied on attach.
    /// </summary>
    /// <exception cref="InvalidOperationException">The call already has an answer.</exception>
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

    /// <summary>
    /// Answers the held call with a <c>refused</c> error. An answer that comes before the server attaches the reply is kept and applied on attach.
    /// </summary>
    /// <param name="message">The text of the error.</param>
    /// <exception cref="InvalidOperationException">The call already has an answer.</exception>
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
            throw new InvalidOperationException("The held call is already answered. Answer a held call once only.");
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
