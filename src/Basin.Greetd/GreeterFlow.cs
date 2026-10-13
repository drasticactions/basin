using System.Buffers;
using System.Text;
using Basin.Capabilities;
using Basin.Seat;

namespace Basin.Greetd;

public sealed class GreeterFlow : IDisposable
{
    private readonly GreetdClient _client;
    private readonly GreetdLogin _login;
    private readonly GreeterMemory _memory;
    private readonly SeatKeyboard? _keyboard;
    private readonly GreeterFlowOptions _options;
    private readonly CompositorSynchronizationContext _context;
    private readonly List<string> _messages = [];
    private GreeterSettings _settings;
    private IReadOnlyList<LoginUser> _users = [];
    private IReadOnlyList<GreetdSession> _sessions = [];
    private IReadOnlySet<uint> _homed = new HashSet<uint>();
    private GreetdSession? _chosen;
    private bool _sessionPicked;
    private (string User, string Display)? _pendingBegin;
    private bool _layoutPicked;
    private bool _disposed;
    private string _command = "";

    public GreeterFlow(ICompositorEventLoop loop, GreetdClient client, GreeterSettings settings,
                       GreeterMemory memory, SeatKeyboard? keyboard, GreeterFlowOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(memory);
        _client = client;
        _settings = settings;
        _memory = memory;
        _keyboard = keyboard;
        _options = options ?? new GreeterFlowOptions();
        _context = new CompositorSynchronizationContext(loop);
        _client.Closed += OnClosed;
        _login = new GreetdLogin(client);
        _login.Changed += OnLoginChanged;
    }

    public GreeterSettings Settings => _settings;

    public GreeterMemory Memory => _memory;

    public IReadOnlyList<LoginUser> Users => _users;

    public IReadOnlyList<GreetdSession> Sessions => _sessions;

    public GreeterPhase Phase { get; private set; } = GreeterPhase.Users;

    public GreetdLoginState LoginState => _login.State;

    public string? SuggestedUser { get; private set; }

    public string? CurrentUser { get; private set; }

    public string? CurrentDisplayName { get; private set; }

    public string? PromptText { get; private set; }

    public bool PromptSecret { get; private set; }

    public IReadOnlyList<string> Messages => _messages;

    public string? Error { get; private set; }

    public bool ErrorNeedsDismiss { get; private set; }

    public string? SelectedSession { get; private set; }

    public string? SelectedLayout { get; private set; }

    public GreetdSession? StartedSession => Phase == GreeterPhase.Started ? _chosen : null;

    public string Command
    {
        get => _command;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_command != value)
            {
                _command = value;
                Changed?.Invoke();
            }
        }
    }

    public event Action? Changed;

    public event Action<GreeterStep>? Stepped;

    public event Action<string>? Closed;

    public void Start()
    {
        ReadUsers();
        ReadSessions();
        if (_options.Homed)
        {
            _ = ReadHomedAsync();
        }

        ResetLayouts();
    }

    public void ShowUsers()
    {
        if (!_settings.ListUsers)
        {
            AskName();
            return;
        }

        _sessionPicked = false;
        _layoutPicked = false;
        Phase = GreeterPhase.Users;
        ClearConversation();
        CurrentUser = null;
        CurrentDisplayName = null;
        SuggestedUser = _users.FirstOrDefault(user => user.Name == _memory.LastUser)?.Name;
        Step(new GreeterStep(GreeterStepKind.UsersShown, SuggestedUser));
        SelectSessionCore(SuggestedUser is null ? null : _memory.SessionOf(SuggestedUser));
        if (SuggestedUser is { } suggested)
        {
            LayoutForUser(suggested);
        }
    }

    public bool Choose(string user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!_settings.ListUsers || _users.FirstOrDefault(listed => listed.Name == user) is not { } entry)
        {
            return false;
        }

        Begin(entry.Name, entry.DisplayName);
        return true;
    }

    public void ChooseOther() => AskName();

    public void EnterName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Trim() is { Length: > 0 } trimmed)
        {
            Begin(trimmed, trimmed);
        }
    }

    public bool Answer(ReadOnlySpan<char> text)
    {
        if (_login.State != GreetdLoginState.Prompting)
        {
            return false;
        }

        var length = Encoding.UTF8.GetMaxByteCount(text.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        try
        {
            var written = Encoding.UTF8.GetBytes(text, buffer);
            _login.Answer(buffer.AsSpan(0, written));
        }
        finally
        {
            buffer.AsSpan().Clear();
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return true;
    }

    public void Cancel()
    {
        _pendingBegin = null;
        if (_login.State is not (GreetdLoginState.Idle or GreetdLoginState.Failed))
        {
            _login.Cancel();
        }

        Error = null;
        ErrorNeedsDismiss = false;
        ShowUsers();
    }

    public void Dismiss()
    {
        Error = null;
        ErrorNeedsDismiss = false;
        ShowUsers();
    }

    public bool SelectSession(string? id, bool byUser)
    {
        if (!byUser)
        {
            SelectSessionCore(id);
            return true;
        }

        if (id is null || _sessions.All(session => session.Id != id))
        {
            return false;
        }

        _sessionPicked = true;
        SelectedSession = id;
        Step(new GreeterStep(GreeterStepKind.SessionSelected, id));
        return true;
    }

    public bool SelectLayout(string id, bool byUser)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (byUser)
        {
            _layoutPicked = true;
        }

        return ApplyLayout(id);
    }

    public void Reload(GreeterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        ReadUsers();
        ReadSessions();
        ResetLayouts();
        if (Phase == GreeterPhase.Users || _login.State is GreetdLoginState.Idle)
        {
            ShowUsers();
        }
    }

    public IEnumerable<string> LayoutEnvironment() =>
        SelectedLayout is { } id ? KeymapNames.Parse(id).EnvironmentLines() : [];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _login.Changed -= OnLoginChanged;
        _client.Closed -= OnClosed;
        _login.Dispose();
        _context.Dispose();
    }

    private void AskName()
    {
        Phase = GreeterPhase.AskingName;
        CurrentUser = null;
        CurrentDisplayName = null;
        ClearConversation();
        Step(new GreeterStep(GreeterStepKind.AskingName));
    }

    private void Begin(string user, string display)
    {
        if (!_login.CanStart)
        {
            if (_login.State == GreetdLoginState.Waiting)
            {
                _pendingBegin = (user, display);
            }
            else if (_login.State is GreetdLoginState.Prompting or GreetdLoginState.Ready)
            {
                _pendingBegin = (user, display);
                _login.Cancel();
            }

            return;
        }

        _pendingBegin = null;

        if (_memory.SessionOf(user) is { } remembered && _sessions.Any(session => session.Id == remembered) && !_sessionPicked)
        {
            SelectSessionCore(remembered);
        }

        LayoutForUser(user);
        Phase = GreeterPhase.Conversation;
        CurrentUser = user;
        CurrentDisplayName = display;
        ClearConversation();
        Step(new GreeterStep(GreeterStepKind.ConversationBegun, display));
        _login.Start(user);
    }

    private void ClearConversation()
    {
        _messages.Clear();
        PromptText = null;
        PromptSecret = false;
        Error = null;
        ErrorNeedsDismiss = false;
    }

    private async Task ReadHomedAsync()
    {
        var homed = await HomedUsers.ListAsync().ConfigureAwait(false);
        _context.Post(_ =>
        {
            if (_disposed)
            {
                return;
            }

            if (homed.Reason is { } reason)
            {
                _options.Log.Debug($"{reason}");
            }

            if (homed.Uids.Count > 0)
            {
                _homed = homed.Uids;
                ReadUsers();
                if (Phase == GreeterPhase.Users)
                {
                    ShowUsers();
                }
            }
        }, null);
    }

    private void ReadUsers()
    {
        var root = _options.Root;
        _users = LoginUsers.Read(new LoginUsersOptions
        {
            PasswdPath = Path.Combine(root, "etc/passwd"),
            LoginDefsPath = Path.Combine(root, "etc/login.defs"),
            IconDirectory = Path.Combine(root, "var/lib/AccountsService/icons"),
            MinUid = _settings.MinUid,
            MaxUid = _settings.MaxUid,
            Hide = _settings.HideUsers,
            HomedUids = _homed,
        });
        Step(new GreeterStep(GreeterStepKind.UsersRead, Count: _users.Count));
    }

    private void ReadSessions()
    {
        var root = _options.Root;
        _sessions = GreetdSessions.List(new GreetdSessionsOptions
        {
            DataDirectories = root == "/" ? null : [Path.Combine(root, "usr/share")],
            X11Wrapper = _settings.X11Wrapper,
            EnvironmentsPath = _settings.Environments,
        });
        Step(new GreeterStep(GreeterStepKind.SessionsRead, Count: _sessions.Count));
        SelectSessionCore(null);
    }

    private void SelectSessionCore(string? id)
    {
        var wanted = id ?? (_settings.DefaultSession.Length > 0 ? _settings.DefaultSession : null);
        var entry = _sessions.FirstOrDefault(session => session.Id == wanted)
            ?? _sessions.FirstOrDefault(session => session.Type == GreetdSessionType.Wayland)
            ?? _sessions.FirstOrDefault();
        SelectedSession = entry?.Id;
        Step(new GreeterStep(GreeterStepKind.SessionSelected, SelectedSession));
    }

    private void ResetLayouts()
    {
        var layouts = _settings.Layouts;
        if (layouts.Count == 0)
        {
            SelectedLayout = null;
            Changed?.Invoke();
            return;
        }

        var system = SystemKeymap.Read();
        var current = SelectedLayout is not null && layouts.Contains(SelectedLayout) ? SelectedLayout : null;
        current ??= layouts.FirstOrDefault(id => KeymapNames.Parse(id) is var names && names.Layout == system.Layout && (names.Variant ?? "") == (system.Variant ?? ""));
        current ??= layouts[0];
        ApplyLayout(current);
    }

    private void LayoutForUser(string user)
    {
        if (!_layoutPicked && _memory.LayoutOf(user) is { } remembered && _settings.Layouts.Contains(remembered) && remembered != SelectedLayout)
        {
            ApplyLayout(remembered);
        }
    }

    private bool ApplyLayout(string id)
    {
        var names = KeymapNames.Parse(id);
        if (_keyboard is not { } keyboard || !keyboard.KeymapSource.TryCompile(names, out _))
        {
            _options.Log.Warn($"keyboard layout {id} does not compile; the keymap is unchanged");
            Step(new GreeterStep(GreeterStepKind.LayoutFailed, id));
            return false;
        }

        keyboard.SetKeymap(names);
        SelectedLayout = id;
        Step(new GreeterStep(GreeterStepKind.LayoutApplied, id));
        return true;
    }

    private void OnLoginChanged()
    {
        var login = _login;
        if (_pendingBegin is { } pending && login.CanStart)
        {
            _pendingBegin = null;
            Begin(pending.User, pending.Display);
            return;
        }

        switch (login.State)
        {
            case GreetdLoginState.Prompting when login.PromptType is GreetdAuthMessageType.Info or GreetdAuthMessageType.Error:
                _messages.Add(login.PromptText ?? "");
                Step(new GreeterStep(GreeterStepKind.Message, login.PromptText ?? "", login.PromptType));
                login.Acknowledge();
                break;
            case GreetdLoginState.Prompting:
                PromptText = login.PromptText ?? "";
                PromptSecret = login.PromptType == GreetdAuthMessageType.Secret;
                Step(new GreeterStep(GreeterStepKind.Prompt, PromptText, login.PromptType));
                break;
            case GreetdLoginState.Ready:
                Step(new GreeterStep(GreeterStepKind.LoginOk));
                StartSession();
                break;
            case GreetdLoginState.Started:
                OnStarted();
                break;
            case GreetdLoginState.Failed when login.FailureKind == GreetdErrorType.AuthError:
                if (login.User is { } user && login.CanStart)
                {
                    Phase = GreeterPhase.Conversation;
                    ClearConversation();
                    Error = _options.LoginIncorrect;
                    Step(new GreeterStep(GreeterStepKind.AuthFailed, Error));
                    login.Start(user);
                }
                else
                {
                    Step(new GreeterStep(GreeterStepKind.AuthFailed));
                }

                break;
            case GreetdLoginState.Failed:
                Phase = GreeterPhase.Error;
                Error = login.FailureDescription ?? "";
                ErrorNeedsDismiss = true;
                Step(new GreeterStep(GreeterStepKind.Failed, Error));
                break;
        }
    }

    private void StartSession()
    {
        var session = Chosen();
        if (session is null)
        {
            Phase = GreeterPhase.Error;
            Error = _options.NoSessions;
            ErrorNeedsDismiss = true;
            Step(new GreeterStep(GreeterStepKind.NoSession, Error));
            _login.Cancel();
            return;
        }

        Phase = GreeterPhase.Starting;
        _chosen = session;
        _login.StartSession(session.Command([.. LayoutEnvironment(), .. _options.Environment]));
    }

    private GreetdSession? Chosen()
    {
        if (_settings.AllowCommand && Command.Trim() is { Length: > 0 } command)
        {
            return GreetdSessions.FreeCommand(command);
        }

        return SelectedSession is { } id ? _sessions.FirstOrDefault(session => session.Id == id) : null;
    }

    private void OnStarted()
    {
        if (_login.User is { } user)
        {
            var free = _chosen?.Type == GreetdSessionType.Command && _chosen.Entry is null && _chosen.Id == "command";
            _memory.Remember(user, free ? null : _chosen?.Id, SelectedLayout);
        }

        Phase = GreeterPhase.Started;
        Step(new GreeterStep(GreeterStepKind.Started, _chosen?.Id));
    }

    private void OnClosed(string reason)
    {
        if (_login.State == GreetdLoginState.Started)
        {
            return;
        }

        Closed?.Invoke(reason);
    }

    private void Step(GreeterStep step)
    {
        Stepped?.Invoke(step);
        Changed?.Invoke();
    }
}
