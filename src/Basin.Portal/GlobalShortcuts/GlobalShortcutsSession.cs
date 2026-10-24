using Basin.Capabilities;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// A GlobalShortcuts portal session. It sends <c>Activated</c> and <c>Deactivated</c> for the shortcuts that the session bound and that belong to its application.
/// </summary>
public sealed class GlobalShortcutsSession : PortalSession, IGlobalShortcutObserver
{
    private readonly PortalGlobalShortcutsModule _owner;
    private readonly List<string> _bound = [];

    internal GlobalShortcutsSession(PortalGlobalShortcutsModule owner, PortalBus bus, ObjectPath handle, string appId)
        : base(bus, handle, appId) => _owner = owner;

    /// <summary>
    /// Gets a value indicating whether the application called <c>BindShortcuts</c> on this session.
    /// </summary>
    public bool BindCalled { get; internal set; }

    /// <summary>
    /// Gets the ids of the shortcuts that the session bound.
    /// </summary>
    public IReadOnlyList<string> BoundIds => _bound;

    internal List<string> BoundList => _bound;

    /// <summary>
    /// Does nothing. The session reports registry changes through its own method replies.
    /// </summary>
    /// <param name="shortcut">The shortcut that was added.</param>
    public void ShortcutRegistered(in GlobalShortcutInfo shortcut)
    {
    }

    /// <summary>
    /// Does nothing. The session reports registry changes through its own method replies.
    /// </summary>
    /// <param name="shortcut">The shortcut that was removed.</param>
    public void ShortcutRemoved(in GlobalShortcutInfo shortcut)
    {
    }

    /// <summary>
    /// Sends <c>Activated</c> when the shortcut belongs to the session's application and the session bound it.
    /// </summary>
    /// <param name="shortcut">The shortcut that was pressed.</param>
    /// <param name="timestampMs">The time of the press, in milliseconds on a monotonic clock.</param>
    public void ShortcutActivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
        if (shortcut.AppId == AppId && _bound.Contains(shortcut.Id))
        {
            Emit(shortcut.Id, timestampMs, activated: true);
        }
    }

    /// <summary>
    /// Sends <c>Deactivated</c> when the shortcut belongs to the session's application and the session bound it.
    /// </summary>
    /// <param name="shortcut">The shortcut that was released.</param>
    /// <param name="timestampMs">The time of the release, in milliseconds on a monotonic clock.</param>
    public void ShortcutDeactivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
        if (shortcut.AppId == AppId && _bound.Contains(shortcut.Id))
        {
            Emit(shortcut.Id, timestampMs, activated: false);
        }
    }

    /// <inheritdoc/>
    protected override void CloseCore() => _owner.Release(this);

    private void Emit(string id, ulong timestampMs, bool activated)
    {
        if (Bus.Connection is not { } connection || IsClosed)
        {
            return;
        }

        try
        {
            if (activated)
            {
                connection.EmitActivated(new ObjectPath(PortalBus.RootPath), Handle, id, timestampMs, []);
            }
            else
            {
                connection.EmitDeactivated(new ObjectPath(PortalBus.RootPath), Handle, id, timestampMs, []);
            }
        }
        catch (Exception e) when (e is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug(e, $"shortcut session {Id}: signal not sent");
        }
    }
}
