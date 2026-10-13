using Basin.Diagnostics;

namespace Basin.Greetd;

public sealed class GreetdLogin : IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly GreetdClient _client;
    private Pending _pending;
    private GreetdError? _failure;
    private bool _sessionOpen;
    private bool _cancelQueued;
    private bool _disposed;

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

    public GreetdLoginState State { get; private set; }

    public string? User { get; private set; }

    public GreetdAuthMessageType PromptType { get; private set; }

    public string? PromptText { get; private set; }

    public GreetdErrorType FailureKind { get; private set; }

    public string? FailureDescription { get; private set; }

    public GreetdClient Client => _client;

    public event Action? Changed;

    public bool CanStart => State is GreetdLoginState.Idle or GreetdLoginState.Failed && !_client.IsClosed;

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
        Send(Pending.Create, new GreetdCreateSession(user));
        _sessionOpen = true;
        Enter(GreetdLoginState.Waiting);
    }

    public void Answer(ReadOnlySpan<byte> utf8Response)
    {
        _thread.Assert();
        if (State != GreetdLoginState.Prompting || PromptType is not (GreetdAuthMessageType.Secret or GreetdAuthMessageType.Visible))
        {
            throw new InvalidOperationException("Answer needs a visible or secret prompt");
        }

        _pending = Pending.Answer;
        _client.SendAuthResponse(utf8Response);
        PromptText = null;
        Enter(GreetdLoginState.Waiting);
    }

    public void Acknowledge()
    {
        _thread.Assert();
        if (State != GreetdLoginState.Prompting || PromptType is not (GreetdAuthMessageType.Info or GreetdAuthMessageType.Error))
        {
            throw new InvalidOperationException("Acknowledge needs an info or error message");
        }

        Send(Pending.Answer, new GreetdPostAuthMessageResponse(null));
        PromptText = null;
        Enter(GreetdLoginState.Waiting);
    }

    public void StartSession(GreetdSessionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _thread.Assert();
        if (State != GreetdLoginState.Ready)
        {
            throw new InvalidOperationException($"StartSession needs Ready, and this login is {State}");
        }

        Send(Pending.Start, new GreetdStartSession(command.Cmd, command.Env));
        Enter(GreetdLoginState.Starting);
    }

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

    private void Send(Pending pending, GreetdRequest request)
    {
        _pending = pending;
        _client.Send(request);
    }

    private void SendCancel()
    {
        _cancelQueued = false;
        PromptText = null;
        Send(Pending.Cancel, new GreetdCancelSession());
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
