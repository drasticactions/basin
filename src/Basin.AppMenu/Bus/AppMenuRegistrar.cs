using System.Text;
using Tmds.DBus.Protocol;
using static Basin.AppMenu.AppMenuLog;

namespace Basin.AppMenu;

/// <summary>
/// Serves <c>com.canonical.AppMenu.Registrar</c> on the <see cref="AppMenuBus"/>. Qt exports a global menu only when a process owns this name.
/// It never takes the name from another owner, for example a Plasma session.
/// </summary>
public sealed class AppMenuRegistrar : IPathMethodHandler, IDisposable
{
    /// <summary>The well-known bus name that the registrar asks for.</summary>
    public const string BusName = "com.canonical.AppMenu.Registrar";

    private const string ObjectPath = "/com/canonical/AppMenu/Registrar";
    private const string Interface = "com.canonical.AppMenu.Registrar";

    private static readonly ReadOnlyMemory<byte> IntrospectXml = Encoding.UTF8.GetBytes(
        """
        <interface name="com.canonical.AppMenu.Registrar">
          <method name="RegisterWindow"><arg name="windowId" type="u" direction="in"/><arg name="menuObjectPath" type="o" direction="in"/></method>
          <method name="UnregisterWindow"><arg name="windowId" type="u" direction="in"/></method>
          <method name="GetMenuForWindow"><arg name="windowId" type="u" direction="in"/><arg name="service" type="s" direction="out"/><arg name="menuObjectPath" type="o" direction="out"/></method>
        </interface>
        """);

    private readonly AppMenuBus _bus;
    private readonly Lock _gate = new();
    private readonly Dictionary<uint, AppMenuAddress> _windows = [];
    private volatile bool _disposed;
    private volatile bool _holdsName;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppMenuRegistrar"/> class and starts to connect and ask for <see cref="BusName"/>.
    /// </summary>
    /// <param name="bus">The bus connection to serve on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bus"/> is <see langword="null"/>.</exception>
    public AppMenuRegistrar(AppMenuBus bus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _ = StartAsync();
    }

    /// <summary>
    /// Gets a value indicating whether this registrar owns <see cref="BusName"/>. It is <see langword="false"/> until <see cref="Ready"/> fires.
    /// </summary>
    public bool OwnsName { get; private set; }

    /// <summary>
    /// Occurs on the compositor thread when a client registers a window, or unregisters one that was registered. The argument is the X11 window id.
    /// </summary>
    public event Action<uint>? Changed;

    /// <summary>
    /// Occurs once on the compositor thread when the name request completes, whether or not the registrar got the name.
    /// It does not fire when the bus is not reachable.
    /// </summary>
    public event Action? Ready;

    string IPathMethodHandler.Path => ObjectPath;

    bool IPathMethodHandler.HandlesChildPaths => false;

    /// <summary>
    /// Finds the dbusmenu address that a client registered for a window. Registrations stay after the client leaves the bus.
    /// </summary>
    /// <param name="windowId">The X11 window id.</param>
    /// <returns>The address, or <see langword="null"/> when the window has no registration.</returns>
    public AppMenuAddress? AddressOf(uint windowId)
    {
        lock (_gate)
        {
            return _windows.GetValueOrDefault(windowId);
        }
    }

    /// <summary>
    /// Removes the registration of a window. The compositor calls it when the X11 window goes away. It raises no event.
    /// </summary>
    /// <param name="windowId">The X11 window id.</param>
    public void Forget(uint windowId)
    {
        lock (_gate)
        {
            _windows.Remove(windowId);
        }
    }

    ValueTask IPathMethodHandler.HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        if (_disposed)
        {
            context.ReplyUnknownMethodError();
            return default;
        }

        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([IntrospectXml], []);
            return default;
        }

        if (request.InterfaceAsString != Interface)
        {
            context.ReplyUnknownMethodError();
            return default;
        }

        var reader = request.GetBodyReader();
        switch (request.MemberAsString)
        {
            case "RegisterWindow" when request.SignatureAsString == "uo":
            {
                var id = reader.ReadUInt32();
                var path = reader.ReadObjectPathAsString();
                var address = AppMenuAddress.DbusMenu(request.SenderAsString ?? "", path);
                lock (_gate)
                {
                    _windows[id] = address;
                }

                ReplyEmpty(context);
                Notify(id);
                return default;
            }

            case "UnregisterWindow" when request.SignatureAsString == "u":
            {
                var id = reader.ReadUInt32();
                bool removed;
                lock (_gate)
                {
                    removed = _windows.Remove(id);
                }

                ReplyEmpty(context);
                if (removed)
                {
                    Notify(id);
                }

                return default;
            }

            case "GetMenuForWindow" when request.SignatureAsString == "u":
            {
                var id = reader.ReadUInt32();
                var address = AddressOf(id);
                if (address is null)
                {
                    context.ReplyError("com.canonical.AppMenu.Registrar.Error.NoMenu", $"window {id} has no registered menu");
                    return default;
                }

                if (!context.NoReplyExpected)
                {
                    using var writer = context.CreateReplyWriter("so");
                    writer.WriteString(address.BusName);
                    writer.WriteObjectPath(address.ObjectPath);
                    context.Reply(writer.CreateMessage());
                }

                return default;
            }

            default:
                context.ReplyUnknownMethodError();
                return default;
        }
    }

    /// <summary>
    /// Forgets every registration, stops raising <see cref="Changed"/>, releases the bus name and stops serving. A second call does nothing.
    /// The release runs in the background, so a registrar created at once can lose the race for the name.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            _windows.Clear();
        }

        _ = StopAsync();
    }

    private static void ReplyEmpty(MethodContext context)
    {
        if (context.NoReplyExpected)
        {
            return;
        }

        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }

    private void Notify(uint id) => _bus.Post(() =>
    {
        if (!_disposed)
        {
            Changed?.Invoke(id);
        }
    });

    private async Task StartAsync()
    {
        try
        {
            if (await _bus.ConnectionAsync().ConfigureAwait(false) is not { } connection || _disposed)
            {
                return;
            }

            connection.AddMethodHandler(this);
            var owns = await connection.TryRequestNameAsync(BusName, RequestNameOptions.None).ConfigureAwait(false);
            _holdsName = owns;
            if (_disposed)
            {
                if (owns)
                {
                    _holdsName = false;
                    await connection.ReleaseNameAsync(BusName).ConfigureAwait(false);
                }

                return;
            }

            _bus.Post(() =>
            {
                OwnsName = owns;
                if (!owns)
                {
                    Log.Info($"registrar: {BusName} is owned by another process, windows register with it");
                }

                Ready?.Invoke();
            });
        }
        catch (Exception error) when (error is DBusExceptionBase or IOException or InvalidOperationException)
        {
            Log.Warn(error, $"registrar: cannot own {BusName}");
        }
    }

    private async Task StopAsync()
    {
        try
        {
            if (await _bus.ConnectionAsync().ConfigureAwait(false) is not { } connection)
            {
                return;
            }

            connection.RemoveMethodHandler(ObjectPath);
            if (_holdsName)
            {
                _holdsName = false;
                await connection.ReleaseNameAsync(BusName).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is DBusConnectionClosedException or ObjectDisposedException)
        {
            Log.Debug($"registrar: the connection closed first, and the bus drops {BusName} with it");
        }
        catch (Exception error) when (error is DBusExceptionBase or IOException or InvalidOperationException)
        {
            Log.Warn(error, $"registrar: cannot release {BusName}");
        }
    }
}
