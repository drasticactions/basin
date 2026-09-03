using Basin.Capabilities;
using Basin.Hypr.Protocol;
using Wayland.Server;

namespace Basin.Hypr;

public sealed class HyprlandLockNotifier : ILockStateObserver, IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly ILockState? _state;
    private readonly List<Notification> _notifications = [];

    public HyprlandLockNotifier(WlServerDisplay display, ILockState? state)
    {
        ArgumentNullException.ThrowIfNull(display);
        _state = state;
        _global = display.CreateGlobal(HyprlandLockNotifierV1.Interface, Version, OnBind);
        _state?.AddObserver(this);
    }

    public int NotificationCount => _notifications.Count;

    public void SessionLocked()
    {
        for (var i = 0; i < _notifications.Count; i++)
        {
            _notifications[i].Locked();
        }
    }

    public void SessionUnlocked()
    {
        for (var i = 0; i < _notifications.Count; i++)
        {
            _notifications[i].Unlocked();
        }
    }

    public void Dispose()
    {
        _state?.RemoveObserver(this);
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new HyprlandLockNotifierV1Resource(client, version, id);
        manager.GetLockNotification += (_, e) =>
        {
            var resource = new HyprlandLockNotificationV1Resource(client, manager.Version, e.Id);
            var notification = new Notification(resource);
            _notifications.Add(notification);
            resource.Destroyed += (_, _) => _notifications.Remove(notification);
            if (_state?.IsLocked == true)
            {
                notification.Locked();
            }
        };
    }

    private sealed class Notification(HyprlandLockNotificationV1Resource resource)
    {
        private bool _locked;

        public void Locked()
        {
            if (_locked)
            {
                return;
            }

            _locked = true;
            if (!resource.IsDestroyed)
            {
                resource.SendLocked();
            }
        }

        public void Unlocked()
        {
            if (!_locked)
            {
                return;
            }

            _locked = false;
            if (!resource.IsDestroyed)
            {
                resource.SendUnlocked();
            }
        }
    }
}
