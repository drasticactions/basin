using Basin.Diagnostics;
using Tmds.DBus.Protocol;
using static Basin.Notifications.NotificationsLog;

namespace Basin.Notifications;

/// <summary>
/// The <c>org.freedesktop.Notifications</c> service, version 1.2 of the specification. It owns the bus name, the model, the expiry timer and the signals.
/// It draws nothing. Every member and event runs on the compositor loop thread.
/// </summary>
public sealed class NotificationServer : IDisposable
{
    /// <summary>
    /// The well-known bus name that the server owns.
    /// </summary>
    public const string BusName = "org.freedesktop.Notifications";

    /// <summary>
    /// The object path that the server exports and that its signals come from.
    /// </summary>
    public const string ObjectPath = NotificationsObject.ObjectPath;

    /// <summary>
    /// The specification version that <c>GetServerInformation</c> returns.
    /// </summary>
    public const string SpecVersion = NotificationsObject.SpecVersion;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly ICompositorEventLoop _loop;
    private readonly CompositorSynchronizationContext _context;
    private readonly NotificationsObject _object;
    private readonly List<Notification> _active = [];
    private readonly Dictionary<uint, long> _deadlines = [];
    private readonly List<uint> _due = [];
    private DBusConnection? _connection;
    private IEventSource? _timer;
    private uint _nextId = 1;
    private volatile bool _disposed;

    private NotificationServer(ICompositorEventLoop loop, NotificationServerOptions options, string? address)
    {
        _loop = loop;
        Options = options;
        Address = address;
        _context = new CompositorSynchronizationContext(loop);
        _object = new NotificationsObject(this, options);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the capability names that <c>GetCapabilities</c> returns.
    /// </summary>
    public static IReadOnlyList<string> Capabilities => NotificationsObject.CapabilityNames;

    /// <summary>
    /// Gets the options that the server was created with.
    /// </summary>
    public NotificationServerOptions Options { get; }

    /// <summary>
    /// Gets the D-Bus address that the server connected to.
    /// </summary>
    public string? Address { get; }

    /// <summary>
    /// Gets a value indicating whether the server owns <see cref="BusName"/>. It goes false when another daemon takes the name or the server is disposed.
    /// </summary>
    public bool OwnsName { get; private set; }

    /// <summary>
    /// Gets the unique bus name of the server connection, or <see langword="null"/> after <see cref="Dispose"/>.
    /// </summary>
    public string? UniqueName => _connection?.UniqueName;

    /// <summary>
    /// Gets the open notifications in the order that they were added. A replacement keeps the position. The list is live, not a copy.
    /// </summary>
    public IReadOnlyList<Notification> Active => _active;

    /// <summary>
    /// Occurs on the loop after a new notification is added to <see cref="Active"/>, from a client or from <see cref="Post(Notification, uint)"/>.
    /// </summary>
    public event Action<Notification>? Added;

    /// <summary>
    /// Occurs on the loop after a notification replaces an open one with the same id. The arguments are the previous record and the new record.
    /// </summary>
    public event Action<Notification, Notification>? Replaced;

    /// <summary>
    /// Occurs on the loop after a notification is removed from <see cref="Active"/>, with the reason it closed. It is not raised by <see cref="Dispose"/>.
    /// </summary>
    public event Action<Notification, NotificationCloseReason>? Closed;

    /// <summary>
    /// Occurs on the loop when <see cref="Invoke(uint, string, string)"/> succeeds, before the notification closes. The arguments are the notification, the action key and the activation token.
    /// </summary>
    public event Action<Notification, string, string?>? ActionInvoked;

    /// <summary>
    /// Occurs on the loop when another daemon takes <see cref="BusName"/>. It can occur only when <see cref="NotificationServerOptions.AllowReplacement"/> is set. <see cref="Active"/> is not cleared.
    /// </summary>
    public event Action? NameLost;

    /// <summary>
    /// Creates a server with the default <see cref="NotificationServerOptions"/>.
    /// </summary>
    /// <param name="loop">The compositor loop that the server runs on.</param>
    /// <param name="reason">The reason for the failure when the method returns <see langword="null"/>, otherwise <see langword="null"/>.</param>
    /// <returns>The server, which owns the bus name, or <see langword="null"/> when there is no bus or another daemon owns the name.</returns>
    public static NotificationServer? TryCreate(ICompositorEventLoop loop, out string? reason) =>
        TryCreate(loop, new NotificationServerOptions(), out reason);

    /// <summary>
    /// Connects to the session bus and requests <see cref="BusName"/> without queuing. It blocks the caller for at most <see cref="NotificationServerOptions.ConnectTimeout"/>.
    /// A missing or silent bus gives <see langword="null"/>, not an exception. Call it on the loop thread.
    /// </summary>
    /// <param name="loop">The compositor loop that the server runs on.</param>
    /// <param name="options">The bus address, the name flags, the expiry default and the server information.</param>
    /// <param name="reason">The reason for the failure when the method returns <see langword="null"/>, for example the process that owns the name. Otherwise <see langword="null"/>.</param>
    /// <returns>The server, which owns the bus name and must be disposed, or <see langword="null"/> on failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static NotificationServer? TryCreate(ICompositorEventLoop loop, NotificationServerOptions options, out string? reason)
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
            reason = "this session has no D-Bus session bus: DBUS_SESSION_BUS_ADDRESS is unset";
            Log.Info($"no notification server: {reason}");
            return null;
        }

        var server = new NotificationServer(loop, options, address);
        string? failure;
        try
        {
            var connecting = Task.Run(() => server.ConnectAsync(address));
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
            server.Dispose();
            reason = failure;
            Log.Info($"no notification server: {reason}");
            return null;
        }

        Log.Info($"{BusName} owned on {address} as {server.UniqueName}");
        reason = null;
        return server;
    }

    /// <summary>
    /// Finds an open notification by id.
    /// </summary>
    /// <param name="id">The notification id.</param>
    /// <returns>The current record, or <see langword="null"/> when no open notification has the id or <paramref name="id"/> is zero.</returns>
    public Notification? Find(uint id)
    {
        var index = IndexOf(id);
        return index < 0 ? null : _active[index];
    }

    /// <summary>
    /// Adds a notification that the shell itself makes. It raises <see cref="Added"/> or <see cref="Replaced"/> but sends no D-Bus signal for it.
    /// <see cref="Notification.Id"/>, <see cref="Notification.Sender"/> and <see cref="Notification.ExpiresAfter"/> are set by the server.
    /// </summary>
    /// <param name="notification">The notification to show. Its <see cref="Notification.ExpireTimeout"/> applies.</param>
    /// <param name="replacesId">The id of an open notification to replace, or zero. An id that is not open gets a new id.</param>
    /// <returns>The id of the added or replaced notification.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="notification"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The server is disposed.</exception>
    public uint Post(Notification notification, uint replacesId = 0)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return Accept(notification with { Sender = null }, replacesId);
    }

    /// <summary>
    /// Closes a notification because the user dismissed it, with <see cref="NotificationCloseReason.Dismissed"/>.
    /// </summary>
    /// <param name="id">The notification id.</param>
    /// <returns><see langword="false"/> when no open notification has the id.</returns>
    public bool Dismiss(uint id) => Close(id, NotificationCloseReason.Dismissed);

    /// <summary>
    /// Closes a notification before its timeout ends, with <see cref="NotificationCloseReason.Expired"/>.
    /// </summary>
    /// <param name="id">The notification id.</param>
    /// <returns><see langword="false"/> when no open notification has the id.</returns>
    public bool Expire(uint id) => Close(id, NotificationCloseReason.Expired);

    /// <summary>
    /// Removes a notification, stops its timeout and raises <see cref="Closed"/>. A notification from a client also gets the <c>NotificationClosed</c> signal.
    /// </summary>
    /// <param name="id">The notification id.</param>
    /// <param name="reason">The reason that the signal and the event carry.</param>
    /// <returns><see langword="false"/> when no open notification has the id.</returns>
    public bool Close(uint id, NotificationCloseReason reason)
    {
        _thread.Assert();
        var index = IndexOf(id);
        if (index < 0)
        {
            return false;
        }

        var notification = _active[index];
        _active.RemoveAt(index);
        if (_deadlines.Remove(id))
        {
            Reschedule();
        }

        if (notification.Sender is not null)
        {
            Signal("NotificationClosed", id, (uint)reason);
        }

        Closed?.Invoke(notification, reason);
        return true;
    }

    /// <summary>
    /// Starts an action. For a client notification it sends <c>ActivationToken</c> when a token is given, then <c>ActionInvoked</c>.
    /// It then raises <see cref="ActionInvoked"/>, and closes the notification as dismissed unless it is <see cref="Notification.Resident"/>.
    /// </summary>
    /// <param name="id">The notification id.</param>
    /// <param name="actionKey">The key of the chosen action.</param>
    /// <param name="activationToken">An xdg-activation token for the client, or <see langword="null"/> to send none.</param>
    /// <returns><see langword="false"/> when no open notification has the id or it has no action with <paramref name="actionKey"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="actionKey"/> is <see langword="null"/>.</exception>
    public bool Invoke(uint id, string actionKey, string? activationToken = null)
    {
        ArgumentNullException.ThrowIfNull(actionKey);
        _thread.Assert();
        var index = IndexOf(id);
        if (index < 0 || !_active[index].HasAction(actionKey))
        {
            return false;
        }

        var notification = _active[index];
        if (notification.Sender is not null)
        {
            if (!string.IsNullOrEmpty(activationToken))
            {
                Signal("ActivationToken", id, activationToken);
            }

            Signal("ActionInvoked", id, actionKey);
        }

        ActionInvoked?.Invoke(notification, actionKey, activationToken);
        if (!notification.Resident)
        {
            Close(id, NotificationCloseReason.Dismissed);
        }

        return true;
    }

    /// <summary>
    /// Closes the bus connection, which releases the name, stops the timer and clears <see cref="Active"/> without raising <see cref="Closed"/>.
    /// A method call that is still on its way to the loop gets an error reply.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _thread.Assert();
        _disposed = true;
        _timer?.Remove();
        _timer = null;
        Interlocked.Exchange(ref _connection, null)?.Dispose();
        OwnsName = false;
        _active.Clear();
        _deadlines.Clear();
        _context.Dispose();
        BasinCounters.Untrack();
    }

    internal uint Accept(Notification notification, uint replacesId)
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var expireMs = notification.ExpireTimeout < 0 ? Options.DefaultExpireMs : notification.ExpireTimeout;
        TimeSpan? expiresAfter = expireMs > 0 ? TimeSpan.FromMilliseconds(expireMs) : null;
        var index = replacesId == 0 ? -1 : IndexOf(replacesId);
        if (index >= 0)
        {
            var previous = _active[index];
            var next = notification with { Id = replacesId, ExpiresAfter = expiresAfter };
            _active[index] = next;
            Arm(replacesId, expireMs);
            Replaced?.Invoke(previous, next);
            return replacesId;
        }

        var added = notification with { Id = NextId(), ExpiresAfter = expiresAfter };
        _active.Add(added);
        Arm(added.Id, expireMs);
        Added?.Invoke(added);
        return added.Id;
    }

    internal void Answer<T>(MethodContext context, T state, Action<NotificationServer, MethodContext, T> work)
    {
        if (_disposed)
        {
            NotificationsObject.TryReplyError(context, "org.freedesktop.DBus.Error.Failed", "the notification server is closing");
            return;
        }

        context.DisposesAsynchronously = true;
        _context.Post(_ =>
        {
            try
            {
                if (_disposed)
                {
                    NotificationsObject.TryReplyError(context, "org.freedesktop.DBus.Error.Failed", "the notification server is closing");
                }
                else
                {
                    work(this, context, state);
                }
            }
            catch (Exception error)
            {
                Log.Warn($"{context.Request.MemberAsString} failed on the loop: {error}");
                NotificationsObject.TryReplyError(context, "org.freedesktop.DBus.Error.Failed", error.Message);
            }
            finally
            {
                try
                {
                    context.Dispose();
                }
                catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException or DBusExceptionBase)
                {
                    Log.Debug($"a method context did not dispose cleanly: {error.Message}");
                }
            }
        }, null);
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
                        Log.Debug($"notification bus: {context.Exception.Message}");
                    }
                },
            });
            await connection.ConnectAsync().ConfigureAwait(false);
            connection.AddMethodHandler(_object);
            var flags = (Options.AllowReplacement ? RequestNameOptions.AllowReplacement : 0)
                | (Options.Replace ? RequestNameOptions.ReplaceExisting : 0);
            Action<string, object?>? onLost = Options.AllowReplacement
                ? (_, _) => _context.Post(_ => OnNameLost(), null)
                : null;
            var owns = await connection.TryRequestNameAsync(BusName, flags, onLost, null, emitOnCapturedContext: false).ConfigureAwait(false);
            if (!owns)
            {
                var owner = await DescribeOwnerAsync(connection).ConfigureAwait(false);
                connection.Dispose();
                return Options.Replace
                    ? $"{BusName} is owned by {owner}, which does not allow replacement"
                    : $"{BusName} is owned by {owner}";
            }

            OwnsName = true;
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
            return $"the session bus at {address} is not reachable: {error.Message}";
        }
    }

    private static async Task<string> DescribeOwnerAsync(DBusConnection connection)
    {
        string unique;
        try
        {
            unique = await connection.CallMethodAsync(
                BusCall(connection, "GetNameOwner", BusName),
                static (Message message, object? _) => message.GetBodyReader().ReadString(),
                null).ConfigureAwait(false);
        }
        catch (DBusExceptionBase)
        {
            return "another process";
        }

        try
        {
            var pid = await connection.CallMethodAsync(
                BusCall(connection, "GetConnectionUnixProcessID", unique),
                static (Message message, object? _) => message.GetBodyReader().ReadUInt32(),
                null).ConfigureAwait(false);
            var comm = File.ReadAllText($"/proc/{pid}/comm").Trim();
            return $"{comm} ({unique}, pid {pid})";
        }
        catch (Exception error) when (error is DBusExceptionBase or IOException or UnauthorizedAccessException)
        {
            return unique;
        }
    }

    private static MessageBuffer BusCall(DBusConnection connection, string member, string argument)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", member, "s");
        writer.WriteString(argument);
        return writer.CreateMessage();
    }

    private void OnNameLost()
    {
        if (_disposed || !OwnsName)
        {
            return;
        }

        OwnsName = false;
        Log.Info($"{BusName} was taken over by another notification server");
        NameLost?.Invoke();
    }

    private void Signal(string member, uint id, uint value)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(null, ObjectPath, NotificationsObject.Interface, member, "uu");
        writer.WriteUInt32(id);
        writer.WriteUInt32(value);
        connection.TrySendMessage(writer.CreateMessage());
    }

    private void Signal(string member, uint id, string value)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        using var writer = connection.GetMessageWriter();
        writer.WriteSignalHeader(null, ObjectPath, NotificationsObject.Interface, member, "us");
        writer.WriteUInt32(id);
        writer.WriteString(value);
        connection.TrySendMessage(writer.CreateMessage());
    }

    private int IndexOf(uint id)
    {
        if (id == 0)
        {
            return -1;
        }

        for (var i = 0; i < _active.Count; i++)
        {
            if (_active[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private uint NextId()
    {
        while (true)
        {
            var id = _nextId++;
            if (_nextId == 0)
            {
                _nextId = 1;
            }

            if (id != 0 && IndexOf(id) < 0)
            {
                return id;
            }
        }
    }

    private void Arm(uint id, int expireMs)
    {
        if (expireMs > 0)
        {
            _deadlines[id] = Environment.TickCount64 + expireMs;
        }
        else
        {
            _deadlines.Remove(id);
        }

        Reschedule();
    }

    private void Reschedule()
    {
        if (_deadlines.Count == 0)
        {
            _timer?.UpdateTimer(0);
            return;
        }

        var earliest = long.MaxValue;
        foreach (var deadline in _deadlines.Values)
        {
            earliest = Math.Min(earliest, deadline);
        }

        var delay = Math.Clamp(earliest - Environment.TickCount64, 1, int.MaxValue);
        _timer ??= _loop.AddTimer(OnTimer);
        _timer.UpdateTimer((int)delay);
    }

    private void OnTimer()
    {
        if (_disposed)
        {
            return;
        }

        var now = Environment.TickCount64;
        _due.Clear();
        foreach (var (id, deadline) in _deadlines)
        {
            if (deadline <= now)
            {
                _due.Add(id);
            }
        }

        foreach (var id in _due)
        {
            Close(id, NotificationCloseReason.Expired);
        }

        _due.Clear();
        Reschedule();
    }
}
