using Basin.Diagnostics;

namespace Basin.Greetd;

/// <summary>
/// The login state machine over one <see cref="GreetdClient"/>. It sends <c>cancel_session</c> after each error, so <see cref="Start"/> works again from <see cref="GreetdLoginState.Failed"/>.
/// Every call must come from the thread that created it.
/// </summary>
public sealed class GreetdLogin : IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly GreetdClient _client;
    private Pending _pending;
    private GreetdError? _failure;
    private bool _sessionOpen;
    private bool _cancelQueued;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GreetdLogin"/> class. It subscribes to the client but does not own it.
    /// </summary>
    /// <param name="client">The connection to greetd. Only this login must send on it.</param>
    public GreetdLogin(GreetdClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _client.Response += OnResponse;
        _client.Closed += OnClosed;
    }

    private enum Pending
    {
        None,
        Create,
        Answer,
        Start,
        Cancel,
    }

    /// <summary>
    /// Gets the current state. Each change raises <see cref="Changed"/>.
    /// </summary>
    public GreetdLoginState State { get; private set; }

    /// <summary>
    /// Gets the user of the current or the last session, or <see langword="null"/> after a cancel completes.
    /// </summary>
    public string? User { get; private set; }

    /// <summary>
    /// Gets the kind of the last auth message. It is valid in <see cref="GreetdLoginState.Prompting"/>.
    /// </summary>
    public GreetdAuthMessageType PromptType { get; private set; }

    /// <summary>
    /// Gets the text of the open auth message, or <see langword="null"/> when no message is open.
    /// </summary>
    public string? PromptText { get; private set; }

    /// <summary>
    /// Gets the kind of the last failure. It is valid in <see cref="GreetdLoginState.Failed"/>.
    /// </summary>
    public GreetdErrorType FailureKind { get; private set; }

    /// <summary>
    /// Gets the description of the last failure, or <see langword="null"/> after <see cref="Start"/>.
    /// </summary>
    public string? FailureDescription { get; private set; }

    /// <summary>
    /// Gets the connection that this login drives.
    /// </summary>
    public GreetdClient Client => _client;

    /// <summary>
    /// Occurs after each state change, also when the new state is the same as the old one. It runs on the loop.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gets a value indicating whether <see cref="Start"/> can begin a session: the state is idle or failed and the connection is open.
    /// </summary>
    public bool CanStart => State is GreetdLoginState.Idle or GreetdLoginState.Failed && !_client.IsClosed;

    /// <summary>
    /// Sends <c>create_session</c> for <paramref name="user"/> and enters <see cref="GreetdLoginState.Waiting"/>, or <see cref="GreetdLoginState.Failed"/> when the write fails.
    /// </summary>
    /// <param name="user">The login name.</param>
    /// <exception cref="ArgumentException"><paramref name="user"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException"><see cref="CanStart"/> is false.</exception>
    public void Start(string user)
    {
        ArgumentException.ThrowIfNullOrEmpty(user);
        _thread.Assert();
        if (!CanStart)
        {
            throw new InvalidOperationException($"a login can start only from Idle or Failed, and this one is {State}");
        }

        User = user;
        FailureDescription = null;
        PromptText = null;
        if (!Send(Pending.Create, new GreetdCreateSession(user)))
        {
            return;
        }

        _sessionOpen = true;
        Enter(GreetdLoginState.Waiting);
    }

    /// <summary>
    /// Answers a visible or secret prompt and enters <see cref="GreetdLoginState.Waiting"/>, or <see cref="GreetdLoginState.Failed"/> when the write fails.
    /// </summary>
    /// <param name="utf8Response">The answer as UTF-8 bytes. The caller keeps ownership and clears it.</param>
    /// <exception cref="InvalidOperationException">No visible or secret prompt is open.</exception>
    public void Answer(ReadOnlySpan<byte> utf8Response)
    {
        _thread.Assert();
        if (State != GreetdLoginState.Prompting || PromptType is not (GreetdAuthMessageType.Secret or GreetdAuthMessageType.Visible))
        {
            throw new InvalidOperationException("Answer needs a visible or secret prompt");
        }

        _pending = Pending.Answer;
        if (!_client.SendAuthResponse(utf8Response))
        {
            return;
        }

        PromptText = null;
        Enter(GreetdLoginState.Waiting);
    }

    /// <summary>
    /// Replies to an info or error message with no answer and enters <see cref="GreetdLoginState.Waiting"/>, or <see cref="GreetdLoginState.Failed"/> when the write fails.
    /// </summary>
    /// <exception cref="InvalidOperationException">No info or error message is open.</exception>
    public void Acknowledge()
    {
        _thread.Assert();
        if (State != GreetdLoginState.Prompting || PromptType is not (GreetdAuthMessageType.Info or GreetdAuthMessageType.Error))
        {
            throw new InvalidOperationException("Acknowledge needs an info or error message");
        }

        if (!Send(Pending.Answer, new GreetdPostAuthMessageResponse(null)))
        {
            return;
        }

        PromptText = null;
        Enter(GreetdLoginState.Waiting);
    }

    /// <summary>
    /// Sends <c>start_session</c> and enters <see cref="GreetdLoginState.Starting"/>, or <see cref="GreetdLoginState.Failed"/> when the write fails. After <see cref="GreetdLoginState.Started"/> the greeter must exit within 5 seconds.
    /// </summary>
    /// <param name="command">The command and the environment of the session.</param>
    /// <exception cref="InvalidOperationException">The state is not <see cref="GreetdLoginState.Ready"/>.</exception>
    public void StartSession(GreetdSessionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _thread.Assert();
        if (State != GreetdLoginState.Ready)
        {
            throw new InvalidOperationException($"StartSession needs Ready, and this login is {State}");
        }

        if (!Send(Pending.Start, new GreetdStartSession(command.Cmd, command.Env)))
        {
            return;
        }

        Enter(GreetdLoginState.Starting);
    }

    /// <summary>
    /// Cancels the open session and goes to <see cref="GreetdLoginState.Idle"/>. When a response is outstanding, the cancel waits for it.
    /// It does nothing when the connection is closed or a session is starting or started.
    /// </summary>
    public void Cancel()
    {
        _thread.Assert();
        if (_client.IsClosed || State is GreetdLoginState.Started or GreetdLoginState.Starting)
        {
            return;
        }

        _failure = null;
        if (_client.IsAwaitingResponse)
        {
            _cancelQueued = true;
            return;
        }

        if (!_sessionOpen)
        {
            User = null;
            Enter(GreetdLoginState.Idle);
            return;
        }

        SendCancel();
    }

    /// <summary>
    /// Unsubscribes from the client. It sends nothing and does not dispose the client.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Response -= OnResponse;
        _client.Closed -= OnClosed;
    }

    private bool Send(Pending pending, GreetdRequest request)
    {
        _pending = pending;
        return _client.Send(request);
    }

    private void SendCancel()
    {
        _cancelQueued = false;
        PromptText = null;
        if (!Send(Pending.Cancel, new GreetdCancelSession()))
        {
            return;
        }

        Enter(GreetdLoginState.Waiting);
    }

    private void OnResponse(GreetdResponse response)
    {
        var pending = _pending;
        _pending = Pending.None;
        if (pending == Pending.Cancel)
        {
            _sessionOpen = false;
            if (_failure is { } failure)
            {
                _failure = null;
                Fail(failure.ErrorType, failure.Description);
            }
            else
            {
                User = null;
                Enter(GreetdLoginState.Idle);
            }

            return;
        }

        if (response is GreetdError error)
        {
            _failure = error;
            if (_sessionOpen)
            {
                SendCancel();
            }
            else
            {
                _failure = null;
                Fail(error.ErrorType, error.Description);
            }

            return;
        }

        if (_cancelQueued)
        {
            SendCancel();
            return;
        }

        switch (response)
        {
            case GreetdAuthMessage message:
                PromptType = message.AuthMessageType;
                PromptText = message.Message;
                Enter(GreetdLoginState.Prompting);
                break;
            case GreetdSuccess when pending == Pending.Start:
                _sessionOpen = false;
                Enter(GreetdLoginState.Started);
                break;
            case GreetdSuccess:
                Enter(GreetdLoginState.Ready);
                break;
        }
    }

    private void OnClosed(string reason)
    {
        _pending = Pending.None;
        _sessionOpen = false;
        _cancelQueued = false;
        if (State != GreetdLoginState.Started)
        {
            Fail(GreetdErrorType.Error, reason);
        }
    }

    private void Fail(GreetdErrorType kind, string description)
    {
        FailureKind = kind;
        FailureDescription = description;
        PromptText = null;
        Enter(GreetdLoginState.Failed);
    }

    private void Enter(GreetdLoginState state)
    {
        State = state;
        Changed?.Invoke();
    }
}
