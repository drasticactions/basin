using Basin.Diagnostics;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public abstract class PortalSession : IDisposable
{
    private bool _closed;

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

    public PortalBus Bus { get; }

    public ObjectPath Handle { get; }

    public string AppId { get; }

    public string Id { get; }

    public bool IsClosed => _closed;

    public event Action? Closed;

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

    protected abstract void CloseCore();

    public void Dispose() => Close(emit: false);
}
