using Basin.Diagnostics;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// The base of a <c>org.freedesktop.impl.portal.Session</c> object. The frontend, the compositor or the loss of the frontend can close it, and it closes once.
/// </summary>
public abstract class PortalSession : IDisposable
{
    private bool _closed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PortalSession"/> class. The caller must pass it to <see cref="PortalBus.Register"/>.
    /// </summary>
    /// <param name="bus">The bus the session lives on.</param>
    /// <param name="handle">The session object path that the frontend gave.</param>
    /// <param name="appId">The application id from the frontend. It can be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bus"/> or <paramref name="appId"/> is <see langword="null"/>.</exception>
    protected PortalSession(PortalBus bus, ObjectPath handle, string appId)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(appId);
        Bus = bus;
        Handle = handle;
        AppId = appId;
        var path = handle.ToString();
        var slash = path.LastIndexOf('/');
        Id = slash >= 0 ? path[(slash + 1)..] : path;
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the bus the session lives on.
    /// </summary>
    public PortalBus Bus { get; }

    /// <summary>
    /// Gets the session object path.
    /// </summary>
    public ObjectPath Handle { get; }

    /// <summary>
    /// Gets the application id that the frontend gave when it created the session.
    /// </summary>
    public string AppId { get; }

    /// <summary>
    /// Gets the last element of <see cref="Handle"/>, for logs and prompts.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets a value indicating whether the session is closed.
    /// </summary>
    public bool IsClosed => _closed;

    /// <summary>
    /// Occurs once, on the loop, after the session releases its resources and leaves the bus.
    /// </summary>
    public event Action? Closed;

    /// <summary>
    /// Closes the session and sends the <c>Closed</c> signal to the frontend. A second call does nothing.
    /// </summary>
    public void Close() => Close(emit: true);

    internal void Close(bool emit)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        try
        {
            CloseCore();
        }
        catch (Exception e)
        {
            Log.Warn($"session {Id}: close failed: {e.Message}");
        }

        Bus.Forget(this);
        if (emit && Bus.Connection is { } connection)
        {
            try
            {
                connection.EmitClosed(Handle);
            }
            catch (Exception e) when (e is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
            {
                Log.Debug($"session {Id}: Closed not sent: {e.Message}");
            }
        }

        Closed?.Invoke();
        Log.Info($"session {Id} for {AppId} closed");
        BasinCounters.Untrack();
    }

    /// <summary>
    /// Releases what the session holds. It runs once, on the first close. An exception from it is logged and does not stop the close.
    /// </summary>
    protected abstract void CloseCore();

    /// <summary>
    /// Closes the session with no <c>Closed</c> signal. A second call does nothing.
    /// </summary>
    public void Dispose() => Close(emit: false);
}
