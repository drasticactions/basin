using Basin.Diagnostics;
using Tmds.DBus.Protocol;
using static Basin.AppMenu.AppMenuLog;

namespace Basin.AppMenu;

/// <summary>
/// One session bus connection that every menu reader and the registrar share. It connects on first use, off the compositor thread, and posts results back to the loop.
/// A missing or silent bus makes the readers fail, and it is never an error.
/// </summary>
public sealed class AppMenuBus : IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly CompositorSynchronizationContext _context;
    private readonly string? _address;
    private Task<DBusConnection?>? _connecting;
    private DBusConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppMenuBus"/> class. It does not connect yet.
    /// </summary>
    /// <param name="loop">The compositor loop that results are posted to.</param>
    /// <param name="address">The D-Bus address, or <see langword="null"/> for the session bus of the environment.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> is <see langword="null"/>.</exception>
    public AppMenuBus(ICompositorEventLoop loop, string? address = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _address = address ?? DBusAddress.Session;
        _context = new CompositorSynchronizationContext(loop);
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets a value indicating whether a bus address is known. When it is <see langword="false"/>, every reader fails.
    /// </summary>
    public bool HasAddress => _address is not null;

    internal Task<DBusConnection?> ConnectionAsync()
    {
        _thread.Assert();
        return _connecting ??= ConnectAsync();
    }

    internal void Post(Action action)
    {
        if (_disposed)
        {
            return;
        }

        _context.Post(_ =>
        {
            if (!_disposed)
            {
                action();
            }
        }, null);
    }

    /// <summary>
    /// Closes the connection and stops posting to the loop. Work posted before is dropped. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection?.Dispose();
        _connection = null;
        _context.Dispose();
        BasinCounters.Untrack();
    }

    private async Task<DBusConnection?> ConnectAsync()
    {
        if (_address is null)
        {
            Log.Info($"this session has no D-Bus session bus; every window gets menus of the compositor's own");
            return null;
        }

        try
        {
            var connection = new DBusConnection(new DBusConnectionOptions(_address)
            {
                AutoConnect = false,
                OnException = context =>
                {
                    if (!_disposed)
                    {
                        Log.Debug($"menu bus: {context.Exception.Message}");
                    }
                },
            });
            await connection.ConnectAsync().ConfigureAwait(false);
            if (_disposed)
            {
                connection.Dispose();
                return null;
            }

            _connection = connection;
            return connection;
        }
        catch (Exception error) when (error is DBusExceptionBase or IOException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            Log.Warn($"the session bus is not reachable ({error.Message}); every window gets menus of the compositor's own");
            return null;
        }
    }
}
