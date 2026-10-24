using Basin.Capabilities;
using Basin.Diagnostics;
using Tmds.DBus.Protocol;
using static Basin.Accessibility.AccessibilityLog;

namespace Basin.Accessibility;

/// <summary>
/// Serves <c>org.freedesktop.a11y.KeyboardMonitor</c> and <c>org.freedesktop.a11y.PointerLocator</c> on the session bus under <see cref="BusName"/>.
/// A screen reader watches keys and takes its own shortcuts through it, and asks what is under the pointer.
/// All state belongs to the compositor loop. Bus calls reach it through a post.
/// </summary>
public sealed class A11yManager : IKeyMonitor, IDisposable
{
    /// <summary>
    /// The well-known bus name.
    /// </summary>
    public const string BusName = "org.freedesktop.a11y.Manager";

    /// <summary>
    /// The object path of both interfaces.
    /// </summary>
    public const string ObjectPath = A11yManagerObject.ObjectPath;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly CompositorSynchronizationContext _context;
    private readonly KeyboardMonitorState _keyboard = new();
    private readonly List<string> _watchers = [];
    private readonly HashSet<string> _pointerClients = new(StringComparer.Ordinal);
    private readonly Basin.Seat.Seat _seat;
    private readonly LayoutPointer _pointer;
    private readonly A11yManagerObject _object;
    private readonly ChromeLocator _chrome;
    private DBusConnection? _connection;
    private bool _disposed;

    private A11yManager(ICompositorEventLoop loop, Basin.Seat.Seat seat, LayoutPointer pointer, string address)
    {
        _context = new CompositorSynchronizationContext(loop);
        _seat = seat;
        _pointer = pointer;
        _object = new A11yManagerObject(this);
        _chrome = new ChromeLocator(address);
        Address = address;
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the address of the session bus.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets the unique name of the manager's bus connection, or <see langword="null"/> after dispose.
    /// </summary>
    public string? UniqueName => _connection?.UniqueName;

    /// <summary>
    /// Gets the number of bus clients that watch keys or hold key grabs.
    /// </summary>
    public int KeyboardClients => _keyboard.ClientCount;

    /// <summary>
    /// Connects to the session bus, takes <see cref="BusName"/> and becomes the seat's <see cref="Basin.Seat.Seat.KeyMonitor"/>.
    /// It blocks the caller for at most <see cref="A11yManagerOptions.ConnectTimeout"/>. Call it on the loop thread.
    /// </summary>
    /// <param name="loop">The compositor loop.</param>
    /// <param name="seat">The seat whose keys the manager sees and whose pointer it reports.</param>
    /// <param name="pointer">The layout pointer, for a pointer over the compositor's own chrome.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <param name="reason">The reason when the method returns <see langword="null"/>, otherwise <see langword="null"/>.</param>
    /// <returns>The manager, which must be disposed, or <see langword="null"/> when there is no bus or another process owns the name.</returns>
    public static A11yManager? TryStart(ICompositorEventLoop loop, Basin.Seat.Seat seat, LayoutPointer pointer, A11yManagerOptions? options, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentNullException.ThrowIfNull(pointer);
        options ??= new A11yManagerOptions();
        var address = options.Address is { Length: > 0 } given ? given : Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(address))
        {
            reason = "this session has no D-Bus session bus";
            Log.Info($"manager: not started, {reason}");
            return null;
        }

        var manager = new A11yManager(loop, seat, pointer, address);
        string? failure;
        try
        {
            var connecting = Task.Run(() => manager.ConnectAsync(address, options.Replace));
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
            manager.Dispose();
            reason = failure;
            Log.Info($"manager: not started, {reason}");
            return null;
        }

        seat.KeyMonitor = manager;
        pointer.Moved += manager.OnPointerMoved;
        reason = null;
        Log.Info($"manager: {BusName} owned on {address} as {manager.UniqueName}");
        return manager;
    }

    /// <inheritdoc/>
    public KeyMonitorVerdict Filter(in KeyMonitorEvent key)
    {
        _thread.Assert();
        if (_disposed || _keyboard.ClientCount == 0)
        {
            return KeyMonitorVerdict.Pass;
        }

        var verdict = _keyboard.Process(key, _watchers);
        if (_watchers.Count > 0 && _connection is { } connection)
        {
            foreach (var name in _watchers)
            {
                SendKeyEvent(connection, name, key);
            }
        }

        return verdict;
    }

    /// <summary>
    /// Releases the bus name, closes the connection and stops monitoring keys. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _thread.Assert();
        _disposed = true;
        if (ReferenceEquals(_seat.KeyMonitor, this))
        {
            _seat.KeyMonitor = null;
        }

        _pointer.Moved -= OnPointerMoved;
        Interlocked.Exchange(ref _connection, null)?.Dispose();
        _chrome.Dispose();
        _keyboard.Clear();
        _pointerClients.Clear();
        _context.Dispose();
        BasinCounters.Untrack();
    }

    internal void Answer(MethodContext context, string sender, Action<KeyboardMonitorState, string> work)
    {
        context.DisposesAsynchronously = true;
        _context.Post(_ =>
        {
            try
            {
                if (_disposed)
                {
                    A11yManagerObject.TryReplyError(context, "org.freedesktop.DBus.Error.Failed", "The accessibility manager is closing.");
                    return;
                }

                work(_keyboard, sender);
                A11yManagerObject.ReplyEmpty(context);
            }
            catch (Exception error)
            {
                Log.Warn(error, $"{context.Request.MemberAsString} failed on the loop");
                A11yManagerObject.TryReplyError(context, "org.freedesktop.DBus.Error.Failed", error.Message);
            }
            finally
            {
                Release(context);
            }
        }, null);
    }

    internal void QueryPointer(MethodContext context, string sender)
    {
        context.DisposesAsynchronously = true;
        _context.Post(_ =>
        {
            if (_disposed)
            {
                A11yManagerObject.TryReplyError(context, "org.freedesktop.DBus.Error.Failed", "The accessibility manager is closing.");
                Release(context);
                return;
            }

            var pointer = _seat.Pointer;
            if (pointer.Focus is { } surface)
            {
                var pid = !surface.IsDestroyed && surface.Resource.Client.TryGetCredentials(out var credentials) ? credentials.Pid : 0;
                Reply(context, sender, pid > 0 ? new PointerAnswer(pid, null, null, pointer.X, pointer.Y) : null);
                return;
            }

            var (x, y) = (_pointer.X, _pointer.Y);
            _ = Task.Run(async () =>
            {
                var answer = await _chrome.FindAsync(x, y).ConfigureAwait(false) ?? new PointerAnswer(0, null, null, x, y);
                _context.Post(_ => Reply(context, sender, answer), null);
            });
        }, null);
    }

    private static void Release(MethodContext context)
    {
        try
        {
            context.Dispose();
        }
        catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException or DBusExceptionBase)
        {
            Log.Debug(error, $"bus: a method context did not dispose cleanly");
        }
    }

    private static void SendKeyEvent(DBusConnection connection, string destination, in KeyMonitorEvent key)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(destination, ObjectPath, A11yManagerObject.KeyboardInterface, "KeyEvent", "buuuq");
        writer.WriteBool(key.Released);
        writer.WriteUInt32(key.Modifiers);
        writer.WriteUInt32(key.Keysym);
        writer.WriteUInt32(key.Unichar);
        writer.WriteUInt16((ushort)(key.Keycode + 8));
        connection.TrySendMessage(writer.CreateMessage());
    }

    private void Reply(MethodContext context, string sender, PointerAnswer? answer)
    {
        try
        {
            if (_disposed)
            {
                A11yManagerObject.TryReplyError(context, "org.freedesktop.DBus.Error.Failed", "The accessibility manager is closing.");
                return;
            }

            if (answer is { } found)
            {
                A11yManagerObject.ReplyPointer(context, found);
            }
            else
            {
                A11yManagerObject.TryReplyError(context, "org.freedesktop.a11y.UnknownToplevel", "The client under the pointer cannot be introspected.");
            }

            _pointerClients.Add(sender);
        }
        catch (Exception error)
        {
            Log.Warn(error, $"QueryPointer failed on the loop");
            A11yManagerObject.TryReplyError(context, "org.freedesktop.DBus.Error.Failed", error.Message);
        }
        finally
        {
            Release(context);
        }
    }

    private void OnPointerMoved()
    {
        if (_pointerClients.Count == 0 || _connection is not { } connection)
        {
            return;
        }

        foreach (var name in _pointerClients)
        {
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(name, ObjectPath, A11yManagerObject.PointerInterface, "PointerPositionChanged");
            connection.TrySendMessage(writer.CreateMessage());
        }

        _pointerClients.Clear();
    }

    private void OnClientGone(string name)
    {
        if (_disposed)
        {
            return;
        }

        _keyboard.Forget(name);
        _pointerClients.Remove(name);
    }

    private async Task<string?> ConnectAsync(string address, bool replace)
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
                        Log.Debug($"bus: connection failed: {context.Exception.Message}");
                    }
                },
            });
            await connection.ConnectAsync().ConfigureAwait(false);
            connection.AddMethodHandler(_object);
            await connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Sender = "org.freedesktop.DBus", Interface = "org.freedesktop.DBus", Member = "NameOwnerChanged" },
                static (Message m, object? _) =>
                {
                    var reader = m.GetBodyReader();
                    var name = reader.ReadString();
                    reader.ReadString();
                    return (Name: name, NewOwner: reader.ReadString());
                },
                (Notification<(string Name, string NewOwner)> change) =>
                {
                    if (change.HasValue && change.Value.Name.StartsWith(':') && change.Value.NewOwner.Length == 0)
                    {
                        var name = change.Value.Name;
                        _context.Post(_ => OnClientGone(name), null);
                    }
                },
                emitOnCapturedContext: false,
                ObserverFlags.None,
                null).ConfigureAwait(false);
            var flags = RequestNameOptions.AllowReplacement | (replace ? RequestNameOptions.ReplaceExisting : 0);
            var owns = await connection.TryRequestNameAsync(BusName, flags, null, null, emitOnCapturedContext: false).ConfigureAwait(false);
            if (!owns)
            {
                connection.Dispose();
                return $"{BusName} is owned by another process";
            }

            _connection = connection;
            if (_disposed)
            {
                Interlocked.Exchange(ref _connection, null)?.Dispose();
            }

            return null;
        }
        catch (Exception error)
        {
            connection?.Dispose();
            Log.Debug($"bus: connect to {address} failed: {error.GetType().Name}");
            return $"the session bus at {address} is not reachable: {error.Message}";
        }
    }
}
