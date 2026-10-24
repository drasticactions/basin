using System.Buffers;
using System.Text;
using Basin.Capabilities;
using Basin.Seat;

namespace Basin.Greetd;

/// <summary>
/// The login flow of a greeter with no UI. It reads the users and the sessions, drives a <see cref="GreetdLogin"/>, applies the keyboard layouts and writes the memory.
/// It reports each transition through <see cref="Stepped"/> and <see cref="Changed"/>. Every call must come from the loop thread.
/// </summary>
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
    private bool _local;
    private byte[]? _held;
    private int _heldLength;
    private bool _startHeld;
    private bool _disposed;
    private string _command = "";

    /// <summary>
    /// Initializes a new instance of the <see cref="GreeterFlow"/> class. It reads nothing and reports nothing until <see cref="Start"/>.
    /// </summary>
    /// <param name="loop">The loop that receives the result of the systemd-homed read.</param>
    /// <param name="client">The greetd connection. The flow does not own it.</param>
    /// <param name="settings">The shared greeter settings.</param>
    /// <param name="memory">The memory to read and to write after a session starts.</param>
    /// <param name="keyboard">The seat keyboard that a layout choice sets, or <see langword="null"/>. With no keyboard, each layout fails.</param>
    /// <param name="options">The texts, the root prefix and the extra environment, or <see langword="null"/> for the defaults.</param>
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

    /// <summary>
    /// Gets the settings in use. <see cref="Reload"/> replaces them.
    /// </summary>
    public GreeterSettings Settings => _settings;

    /// <summary>
    /// Gets the memory that the flow reads and writes.
    /// </summary>
    public GreeterMemory Memory => _memory;

    /// <summary>
    /// Gets the listed users. It is empty before <see cref="Start"/>.
    /// </summary>
    public IReadOnlyList<LoginUser> Users => _users;

    /// <summary>
    /// Gets the sessions to offer. It is empty before <see cref="Start"/>.
    /// </summary>
    public IReadOnlyList<GreetdSession> Sessions => _sessions;

    /// <summary>
    /// Gets the screen that the program must show.
    /// </summary>
    public GreeterPhase Phase { get; private set; } = GreeterPhase.Users;

    /// <summary>
    /// Gets the state of the greetd conversation under the flow.
    /// </summary>
    public GreetdLoginState LoginState => _login.State;

    /// <summary>
    /// Gets the remembered user when that user is listed, or <see langword="null"/>. It is set when the user list shows.
    /// </summary>
    public string? SuggestedUser { get; private set; }

    /// <summary>
    /// Gets the login name of the conversation, or <see langword="null"/> when no user is chosen.
    /// </summary>
    public string? CurrentUser { get; private set; }

    /// <summary>
    /// Gets the name to show for <see cref="CurrentUser"/>, or <see langword="null"/>.
    /// </summary>
    public string? CurrentDisplayName { get; private set; }

    /// <summary>
    /// Gets the text of the open prompt, local or from greetd, or <see langword="null"/> when no prompt is open.
    /// </summary>
    public string? PromptText { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the open prompt wants a hidden answer.
    /// </summary>
    public bool PromptSecret { get; private set; }

    /// <summary>
    /// Gets the info and error lines of the current conversation. The flow acknowledges each one.
    /// </summary>
    public IReadOnlyList<string> Messages => _messages;

    /// <summary>
    /// Gets the error to show, or <see langword="null"/>.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Error"/> stays until <see cref="Dismiss"/>. An authentication failure does not need a dismiss.
    /// </summary>
    public bool ErrorNeedsDismiss { get; private set; }

    /// <summary>
    /// Gets the id of the selected session, or <see langword="null"/> when no session exists.
    /// </summary>
    public string? SelectedSession { get; private set; }

    /// <summary>
    /// Gets the id of the keyboard layout in use, or <see langword="null"/> when the settings offer no layout.
    /// </summary>
    public string? SelectedLayout { get; private set; }

    /// <summary>
    /// Gets the session that greetd accepted, or <see langword="null"/> before <see cref="GreeterPhase.Started"/>.
    /// </summary>
    public GreetdSession? StartedSession => Phase == GreeterPhase.Started ? _chosen : null;

    /// <summary>
    /// Gets or sets the typed command. When <see cref="GreeterSettings.AllowCommand"/> is true and it is not blank, it runs instead of the selected session. A change raises <see cref="Changed"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Occurs after each step and after each change that the view shows.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Occurs for each transition, before <see cref="Changed"/>.
    /// </summary>
    public event Action<GreeterStep>? Stepped;

    /// <summary>
    /// Occurs when the greetd connection closes before a session starts. The argument is the reason.
    /// </summary>
    public event Action<string>? Closed;

    /// <summary>
    /// Reads the users and the sessions, applies the first layout, and starts the systemd-homed read when <see cref="GreeterFlowOptions.Homed"/> is true. Call it after the program subscribes.
    /// </summary>
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

    /// <summary>
    /// Shows the user list with the remembered user as the suggestion, and selects that user's session and layout. When the settings do not list users, it asks for a name.
    /// </summary>
    public void ShowUsers()
    {
        if (!_settings.ListUsers)
        {
            AskName();
            return;
        }

        _sessionPicked = false;
        _layoutPicked = false;
        _local = false;
        DropHeld();
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

    /// <summary>
    /// Begins the login of a listed user at the local secret prompt. A conversation in progress is cancelled first. Nothing reaches greetd until <see cref="Answer"/>.
    /// </summary>
    /// <param name="user">The login name.</param>
    /// <returns><see langword="false"/> when the user is not listed or the settings do not list users.</returns>
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

    /// <summary>
    /// Cancels a conversation in progress and asks for a user name.
    /// </summary>
    public void ChooseOther()
    {
        EndConversation();
        AskName();
    }

    /// <summary>
    /// Begins the login of a typed user name at the local secret prompt. A blank name does nothing.
    /// </summary>
    /// <param name="name">The user name. Leading and trailing white space is removed.</param>
    public void EnterName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Trim() is { Length: > 0 } trimmed)
        {
            Begin(trimmed, trimmed);
        }
    }

    /// <summary>
    /// Answers the open prompt. At the local prompt it holds the text and opens the greetd conversation, then gives the text to greetd's first secret prompt.
    /// </summary>
    /// <param name="text">The answer. The flow clears its encoded copy after the send.</param>
    /// <returns><see langword="false"/> when no prompt is open.</returns>
    public bool Answer(ReadOnlySpan<char> text)
    {
        if (_local)
        {
            if (CurrentUser is not { } user)
            {
                return false;
            }

            _local = false;
            DropHeld();
            _held = ArrayPool<byte>.Shared.Rent(Math.Max(Encoding.UTF8.GetMaxByteCount(text.Length), 1));
            _heldLength = Encoding.UTF8.GetBytes(text, _held);
            if (_login.CanStart)
            {
                _login.Start(user);
            }
            else
            {
                _startHeld = true;
            }

            return true;
        }

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

    /// <summary>
    /// Cancels the conversation, clears the error and shows the user list.
    /// </summary>
    public void Cancel()
    {
        EndConversation();
        Error = null;
        ErrorNeedsDismiss = false;
        ShowUsers();
    }

    /// <summary>
    /// Clears the error and shows the user list. It does not cancel a conversation.
    /// </summary>
    public void Dismiss()
    {
        Error = null;
        ErrorNeedsDismiss = false;
        ShowUsers();
    }

    /// <summary>
    /// Selects a session. A choice by the user is kept over a remembered session. A choice not by the user falls back to the default, then to the first Wayland session.
    /// </summary>
    /// <param name="id">The session id, or <see langword="null"/> for the default when <paramref name="byUser"/> is false.</param>
    /// <param name="byUser">Whether the person picked the session.</param>
    /// <returns><see langword="false"/> when <paramref name="byUser"/> is true and no session has the id. Otherwise <see langword="true"/>.</returns>
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

    /// <summary>
    /// Compiles a keyboard layout and sets it on the seat keyboard. A choice by the user is kept over a remembered layout.
    /// </summary>
    /// <param name="id">The layout id, for example <c>us(intl)</c>. It does not have to be in <see cref="GreeterSettings.Layouts"/>.</param>
    /// <param name="byUser">Whether the person picked the layout.</param>
    /// <returns><see langword="false"/> when the layout does not compile or no keyboard is attached.</returns>
    public bool SelectLayout(string id, bool byUser)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (byUser)
        {
            _layoutPicked = true;
        }

        return ApplyLayout(id);
    }

    /// <summary>
    /// Replaces the settings and reads the users, the sessions and the layouts again. The user list shows again unless a conversation is in progress.
    /// </summary>
    /// <param name="settings">The new settings.</param>
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

    /// <summary>
    /// Gives the <c>XKB_DEFAULT_LAYOUT</c> and <c>XKB_DEFAULT_VARIANT</c> lines for the selected layout.
    /// </summary>
    /// <returns>The <c>NAME=value</c> lines, or none when no layout is selected.</returns>
    public IEnumerable<string> LayoutEnvironment() =>
        SelectedLayout is { } id ? KeymapNames.Parse(id).EnvironmentLines() : [];

    /// <summary>
    /// Clears a held answer, unsubscribes from the client and disposes the login. It does not dispose the client. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DropHeld();
        _login.Changed -= OnLoginChanged;
        _client.Closed -= OnClosed;
        _login.Dispose();
        _context.Dispose();
    }

    private void EndConversation()
    {
        _pendingBegin = null;
        _local = false;
        DropHeld();
        if (_login.State is not (GreetdLoginState.Idle or GreetdLoginState.Failed))
        {
            _login.Cancel();
        }
    }

    private void DropHeld()
    {
        _startHeld = false;
        if (_held is { } held)
        {
            held.AsSpan().Clear();
            ArrayPool<byte>.Shared.Return(held);
            _held = null;
            _heldLength = 0;
        }
    }

    private void AskLocally()
    {
        _local = true;
        PromptText = _options.PasswordPrompt;
        PromptSecret = true;
        Step(new GreeterStep(GreeterStepKind.Prompt, PromptText, GreetdAuthMessageType.Secret));
    }

    private void AskName()
    {
        _local = false;
        DropHeld();
        Phase = GreeterPhase.AskingName;
        CurrentUser = null;
        CurrentDisplayName = null;
        ClearConversation();
        Step(new GreeterStep(GreeterStepKind.AskingName));
    }

    private void Begin(string user, string display)
    {
        _local = false;
        DropHeld();
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
        AskLocally();
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
        if (_startHeld && login.CanStart && CurrentUser is { } waiting)
        {
            _startHeld = false;
            login.Start(waiting);
            return;
        }

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
            case GreetdLoginState.Prompting when login.PromptType == GreetdAuthMessageType.Secret && _held is { } held:
                var length = _heldLength;
                _held = null;
                _heldLength = 0;
                try
                {
                    login.Answer(held.AsSpan(0, length));
                }
                finally
                {
                    held.AsSpan().Clear();
                    ArrayPool<byte>.Shared.Return(held);
                }

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
                DropHeld();
                if (login.User is not null && login.CanStart)
                {
                    Phase = GreeterPhase.Conversation;
                    ClearConversation();
                    Error = _options.LoginIncorrect;
                    Step(new GreeterStep(GreeterStepKind.AuthFailed, Error));
                    AskLocally();
                }
                else
                {
                    Step(new GreeterStep(GreeterStepKind.AuthFailed));
                }

                break;
            case GreetdLoginState.Failed:
                DropHeld();
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
