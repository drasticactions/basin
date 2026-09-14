using Basin.Capabilities;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class GlobalShortcutsSession : PortalSession, IGlobalShortcutObserver
{
    private readonly PortalGlobalShortcutsModule _owner;
    private readonly List<string> _bound = [];

    internal GlobalShortcutsSession(PortalGlobalShortcutsModule owner, PortalBus bus, ObjectPath handle, string appId)
        : base(bus, handle, appId) => _owner = owner;

    public bool BindCalled { get; internal set; }

    public IReadOnlyList<string> BoundIds => _bound;

    internal List<string> BoundList => _bound;

    public void ShortcutRegistered(in GlobalShortcutInfo shortcut)
    {
    }

    public void ShortcutRemoved(in GlobalShortcutInfo shortcut)
    {
    }

    public void ShortcutActivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
        if (shortcut.AppId == AppId && _bound.Contains(shortcut.Id))
        {
            Emit(shortcut.Id, timestampMs, activated: true);
        }
    }

    public void ShortcutDeactivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
        if (shortcut.AppId == AppId && _bound.Contains(shortcut.Id))
        {
            Emit(shortcut.Id, timestampMs, activated: false);
        }
    }

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
            Log.Debug($"shortcut session {Id}: signal not sent: {e.Message}");
        }
    }
}
