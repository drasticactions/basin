using Basin.Diagnostics;
using Tmds.DBus.Protocol;
using static Basin.SoftKeyboard.Fcitx5.Fcitx5Log;

namespace Basin.SoftKeyboard.Fcitx5;

/// <summary>
/// Serves fcitx5's on-screen keyboard panel on the session bus and sends keys to fcitx5's virtual keyboard backend. Every event is raised on the compositor loop.
/// </summary>
public sealed class Fcitx5Keyboard : IKeyboardBackend, IDisposable
{
    /// <summary>
    /// The panel bus name that this object owns. fcitx5 counts the on-screen keyboard as available while the name has an owner.
    /// </summary>
    public const string BusName = "org.fcitx.Fcitx5.VirtualKeyboard";
    /// <summary>
    /// The bus name that fcitx5 registers only while it is in its on-screen keyboard mode.
    /// </summary>
    public const string BackendName = "org.fcitx.Fcitx5.VirtualKeyboardBackend";
    /// <summary>
    /// The object path of the backend and of fcitx5's virtual keyboard service.
    /// </summary>
    public const string BackendPath = "/virtualkeyboard";
    /// <summary>
    /// The backend interface that receives keys, candidate choices, page changes and visibility.
    /// </summary>
    public const string BackendInterface = "org.fcitx.Fcitx5.VirtualKeyboardBackend1";
    /// <summary>
    /// The bus name of the fcitx5 service that switches the on-screen keyboard mode on and off.
    /// </summary>
    public const string ServiceName = "org.fcitx.Fcitx5";
    /// <summary>
    /// The interface on <see cref="ServiceName"/> that has ShowVirtualKeyboard and HideVirtualKeyboard.
    /// </summary>
    public const string ServiceInterface = "org.fcitx.Fcitx.VirtualKeyboard1";

    private const uint FullFunctionMode = 1;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly CompositorSynchronizationContext _context;
    private readonly PanelObject _panel;
    private DBusConnection? _connection;
    private IDisposable? _watch;
    private volatile bool _disposed;

    private Fcitx5Keyboard(ICompositorEventLoop loop, string address)
    {
        Address = address;
        _context = new CompositorSynchronizationContext(loop);
        _panel = new PanelObject(this);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the D-Bus address that the keyboard connected to.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets a value indicating whether this object owns <see cref="BusName"/>. It is <see langword="false"/> after <see cref="Dispose"/>.
    /// </summary>
    public bool OwnsName { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="BackendName"/> has an owner now. The value follows NameOwnerChanged on the compositor loop.
    /// </summary>
    public bool BackendPresent { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether a text field has focus. The consumer sets it, because fcitx5 drops keys that have no focused input context.
    /// </summary>
    public bool HasFocusedField { get; set; }

    /// <summary>
    /// Gets a value indicating whether keys go to fcitx5. It is <see langword="true"/> when the name is owned, the backend is present, a field has focus, and the object is not disposed.
    /// </summary>
    public bool IsActive => OwnsName && BackendPresent && HasFocusedField && !_disposed;

    /// <summary>
    /// Gets the last candidate page from fcitx5. It is <see cref="Fcitx5Candidates.Empty"/> before the first page and after the backend goes away.
    /// </summary>
    public Fcitx5Candidates Candidates { get; private set; } = Fcitx5Candidates.Empty;

    /// <summary>
    /// Gets a value indicating whether a page with one or more words has arrived in this session. It does not go back to <see langword="false"/>.
    /// </summary>
    public bool CandidatesArrived { get; private set; }

    /// <summary>
    /// Gets the preedit text that fcitx5 composes now. It is empty when nothing is composed.
    /// </summary>
    public string Preedit { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the caret position in <see cref="Preedit"/> that fcitx5 sent last, or -1 before the first update.
    /// </summary>
    public int PreeditCaret { get; private set; } = -1;

    /// <summary>
    /// Gets the name of the active fcitx5 input method, or <see langword="null"/> when none is known.
    /// </summary>
    public string? InputMethod { get; private set; }

    /// <summary>
    /// Occurs when <see cref="BackendPresent"/> changes. The argument is the new value.
    /// </summary>
    public event Action<bool>? BackendChanged;

    /// <summary>
    /// Occurs when <see cref="Candidates"/> changes, and when <see cref="ClearCandidates"/> clears it.
    /// </summary>
    public event Action? CandidatesChanged;

    /// <summary>
    /// Occurs when <see cref="Preedit"/> or <see cref="PreeditCaret"/> changes.
    /// </summary>
    public event Action? PreeditChanged;

    /// <summary>
    /// Occurs when <see cref="InputMethod"/> changes. The argument is the new name, or <see langword="null"/> when the active input method was deactivated.
    /// </summary>
    public event Action<string?>? InputMethodChanged;

    /// <summary>
    /// Occurs when fcitx5 calls ShowVirtualKeyboard or HideVirtualKeyboard on the panel. The argument is <see langword="true"/> for show. The consumer decides what to do.
    /// </summary>
    public event Action<bool>? ShowRequested;

    /// <summary>
    /// Connects to the session bus with the default <see cref="Fcitx5KeyboardOptions"/>. This call blocks for at most the connect timeout.
    /// </summary>
    /// <param name="loop">The compositor loop that receives every event.</param>
    /// <param name="reason">The cause of the failure, or <see langword="null"/> on success.</param>
    /// <returns>The keyboard, or <see langword="null"/> when the bus is not reachable or another process owns <see cref="BusName"/>.</returns>
    public static Fcitx5Keyboard? TryCreate(ICompositorEventLoop loop, out string? reason) =>
        TryCreate(loop, new Fcitx5KeyboardOptions(), out reason);

    /// <summary>
    /// Connects to the bus, takes <see cref="BusName"/> without queuing, and starts to watch <see cref="BackendName"/>. This call blocks for at most <see cref="Fcitx5KeyboardOptions.ConnectTimeout"/>.
    /// </summary>
    /// <param name="loop">The compositor loop that receives every event.</param>
    /// <param name="options">The bus address and the connect timeout.</param>
    /// <param name="reason">The cause of the failure, or <see langword="null"/> on success.</param>
    /// <returns>The keyboard, or <see langword="null"/> when no address is known, the bus does not answer, or another process owns <see cref="BusName"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static Fcitx5Keyboard? TryCreate(ICompositorEventLoop loop, Fcitx5KeyboardOptions options, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(options);
        var address = options.Address;
        if (string.IsNullOrEmpty(address))
        {
            address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        }

        if (string.IsNullOrEmpty(address))
        {
            reason = "no session bus: DBUS_SESSION_BUS_ADDRESS is unset";
            return null;
        }

        var keyboard = new Fcitx5Keyboard(loop, address);
        string? failure;
        try
        {
            var connecting = Task.Run(() => keyboard.ConnectAsync(address));
            failure = connecting.Wait(options.ConnectTimeout)
                ? connecting.Result
                : $"the session bus at {address} did not answer within {options.ConnectTimeout.TotalSeconds:0.#} s";
        }
        catch (AggregateException error)
        {
            failure = $"the session bus at {address} is not reachable: {error.InnerException?.Message ?? error.Message}";
        }

        if (failure is not null)
        {
            keyboard.Dispose();
            reason = failure;
            return null;
        }

        reason = null;
        return keyboard;
    }

    /// <summary>
    /// Sends ProcessKeyEvent to the backend without waiting for a reply. It does nothing when the backend is not present.
    /// </summary>
    /// <param name="keysym">The xkb keysym of the key.</param>
    /// <param name="keycode">The xkb keycode, which is the evdev code plus 8.</param>
    /// <param name="state">The xkb modifier mask.</param>
    /// <param name="release"><see langword="true"/> for a release, <see langword="false"/> for a press.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    public void ProcessKey(uint keysym, uint keycode, uint state, bool release, uint timeMs)
    {
        if (_connection is not { } connection || !BackendPresent)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(BackendName, BackendPath, BackendInterface, "ProcessKeyEvent", "uuubu", MessageFlags.NoReplyExpected);
        writer.WriteUInt32(keysym);
        writer.WriteUInt32(keycode);
        writer.WriteUInt32(state);
        writer.WriteBool(release);
        writer.WriteUInt32(timeMs);
        connection.TrySendMessage(writer.CreateMessage());
    }

    /// <summary>
    /// Asks the backend to commit one word of the current page. It does nothing when the backend is not present.
    /// </summary>
    /// <param name="index">The index of the word in <see cref="Fcitx5Candidates.Words"/>.</param>
    public void SelectCandidate(int index) => CallBackend("SelectCandidate", index);

    /// <summary>
    /// Asks the backend for the previous candidate page. It does nothing when the backend is not present.
    /// </summary>
    public void PreviousPage() => CallBackend("PrevPage");

    /// <summary>
    /// Asks the backend for the next candidate page. It does nothing when the backend is not present.
    /// </summary>
    public void NextPage() => CallBackend("NextPage");

    /// <summary>
    /// Calls ShowVirtualKeyboard or HideVirtualKeyboard on fcitx5's service, which switches its on-screen keyboard mode. It also sends ProcessVisibilityEvent when the backend is present.
    /// </summary>
    /// <param name="visible"><see langword="true"/> when the keyboard shows.</param>
    public void SetVisible(bool visible)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(ServiceName, BackendPath, ServiceInterface, visible ? "ShowVirtualKeyboard" : "HideVirtualKeyboard", null, MessageFlags.NoReplyExpected);
            connection.TrySendMessage(writer.CreateMessage());
        }

        if (BackendPresent)
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(BackendName, BackendPath, BackendInterface, "ProcessVisibilityEvent", "b", MessageFlags.NoReplyExpected);
            writer.WriteBool(visible);
            connection.TrySendMessage(writer.CreateMessage());
        }
    }

    /// <summary>
    /// Empties <see cref="Candidates"/> and <see cref="Preedit"/> locally and raises both change events. It sends nothing to fcitx5 and does nothing when both are already empty.
    /// </summary>
    public void ClearCandidates()
    {
        _thread.Assert();
        if (Candidates.Words.Count == 0 && Preedit.Length == 0)
        {
            return;
        }

        Candidates = Fcitx5Candidates.Empty;
        Preedit = string.Empty;
        CandidatesChanged?.Invoke();
        PreeditChanged?.Invoke();
    }

    /// <summary>
    /// Closes the bus connection, which releases <see cref="BusName"/>. No event is raised after this call.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watch?.Dispose();
        _watch = null;
        Interlocked.Exchange(ref _connection, null)?.Dispose();
        OwnsName = false;
        BackendPresent = false;
        _context.Dispose();
        BasinCounters.Untrack();
    }

    internal void Post(Action<Fcitx5Keyboard> work)
    {
        if (_disposed)
        {
            return;
        }

        _context.Post(_ =>
        {
            if (!_disposed)
            {
                work(this);
            }
        }, null);
    }

    internal void OnShowRequested(bool show) => ShowRequested?.Invoke(show);

    internal void OnPreeditCaret(int caret)
    {
        PreeditCaret = caret;
        PreeditChanged?.Invoke();
    }

    internal void OnPreedit(string preedit)
    {
        Preedit = preedit;
        PreeditChanged?.Invoke();
    }

    internal void OnCandidates(Fcitx5Candidates candidates)
    {
        Candidates = candidates;
        if (candidates.Words.Count > 0)
        {
            CandidatesArrived = true;
        }

        CandidatesChanged?.Invoke();
    }

    internal void OnInputMethod(string name)
    {
        InputMethod = name;
        InputMethodChanged?.Invoke(name);
    }

    internal void OnInputMethodGone(string name)
    {
        if (InputMethod == name)
        {
            InputMethod = null;
            InputMethodChanged?.Invoke(null);
        }
    }

    private void OnBackendOwner(string? owner)
    {
        var present = !string.IsNullOrEmpty(owner);
        if (present == BackendPresent)
        {
            return;
        }

        BackendPresent = present;
        Log.Info($"fcitx5: virtual keyboard backend {(present ? $"appeared as {owner}" : "went away")}");
        if (present)
        {
            CallBackend("SetVirtualKeyboardFunctionMode", FullFunctionMode);
            if (_connection is { } connection)
            {
                _ = QueryInputMethodAsync(connection);
            }
        }
        else
        {
            Candidates = Fcitx5Candidates.Empty;
            Preedit = string.Empty;
        }

        BackendChanged?.Invoke(present);
    }

    private void CallBackend(string member)
    {
        if (_connection is not { } connection || !BackendPresent)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(BackendName, BackendPath, BackendInterface, member, null, MessageFlags.NoReplyExpected);
        connection.TrySendMessage(writer.CreateMessage());
    }

    private void CallBackend(string member, int value)
    {
        if (_connection is not { } connection || !BackendPresent)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(BackendName, BackendPath, BackendInterface, member, "i", MessageFlags.NoReplyExpected);
        writer.WriteInt32(value);
        connection.TrySendMessage(writer.CreateMessage());
    }

    private void CallBackend(string member, uint value)
    {
        if (_connection is not { } connection || !BackendPresent)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(BackendName, BackendPath, BackendInterface, member, "u", MessageFlags.NoReplyExpected);
        writer.WriteUInt32(value);
        connection.TrySendMessage(writer.CreateMessage());
    }

    private async Task<string?> ConnectAsync(string address)
    {
        DBusConnection? connection = null;
        try
        {
            connection = new DBusConnection(new DBusConnectionOptions(address)
            {
                AutoConnect = false,
                OnException = context =>
                {
                    if (!_disposed)
                    {
                        Log.Debug(context.Exception, $"fcitx5: bus connection error");
                    }
                },
            });
            await connection.ConnectAsync().ConfigureAwait(false);
            connection.AddMethodHandler(_panel);
            if (!await connection.TryRequestNameAsync(BusName, RequestNameOptions.None).ConfigureAwait(false))
            {
                var owner = await OwnerAsync(connection, BusName).ConfigureAwait(false);
                connection.Dispose();
                return $"{BusName} is owned by {owner ?? "another process"}";
            }

            _watch = await connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Sender = "org.freedesktop.DBus",
                    Path = "/org/freedesktop/DBus",
                    Interface = "org.freedesktop.DBus",
                    Member = "NameOwnerChanged",
                    Arg0 = BackendName,
                },
                static (Message message, object? _) =>
                {
                    var reader = message.GetBodyReader();
                    reader.ReadString();
                    reader.ReadString();
                    return reader.ReadString();
                },
                (Action<Notification<string>>)(notification =>
                {
                    if (notification.HasValue)
                    {
                        var owner = notification.Value;
                        Post(keyboard => keyboard.OnBackendOwner(owner));
                    }
                }),
                false, ObserverFlags.None, null).ConfigureAwait(false);
            var current = await OwnerAsync(connection, BackendName).ConfigureAwait(false);
            OwnsName = true;
            _connection = connection;
            if (_disposed)
            {
                Interlocked.Exchange(ref _connection, null)?.Dispose();
                return "closed while connecting";
            }

            Post(keyboard => keyboard.OnBackendOwner(current));
            return null;
        }
        catch (Exception error) when (error is DBusExceptionBase or IOException or System.Net.Sockets.SocketException or InvalidOperationException or ObjectDisposedException)
        {
            connection?.Dispose();
            return $"the session bus at {address} is not reachable: {error.Message}";
        }
    }

    private async Task QueryInputMethodAsync(DBusConnection connection)
    {
        try
        {
            var name = await connection.CallMethodAsync(
                ControllerCall(connection),
                static (Message message, object? _) => message.GetBodyReader().ReadString(),
                null).ConfigureAwait(false);
            Post(keyboard =>
            {
                if (keyboard.InputMethod is null && name.Length > 0)
                {
                    keyboard.OnInputMethod(name);
                }
            });
        }
        catch (Exception error) when (error is DBusExceptionBase or ObjectDisposedException)
        {
            Log.Debug(error, $"fcitx5: CurrentInputMethod failed");
        }
    }

    private static MessageBuffer ControllerCall(DBusConnection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(ServiceName, "/controller", "org.fcitx.Fcitx.Controller1", "CurrentInputMethod", null);
        return writer.CreateMessage();
    }

    private static async Task<string?> OwnerAsync(DBusConnection connection, string name)
    {
        try
        {
            return await connection.CallMethodAsync(
                OwnerCall(connection, name),
                static (Message message, object? _) => message.GetBodyReader().ReadString(),
                null).ConfigureAwait(false);
        }
        catch (DBusExceptionBase)
        {
            return null;
        }
    }

    private static MessageBuffer OwnerCall(DBusConnection connection, string name)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetNameOwner", "s");
        writer.WriteString(name);
        return writer.CreateMessage();
    }
}
