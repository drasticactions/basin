using Basin.Capabilities;
using Basin.Hypr.Protocol;
using Wayland.Server;

namespace Basin.Hypr;

/// <summary>
/// Advertises <c>hyprland_lock_notifier_v1</c> and sends <c>locked</c> and <c>unlocked</c> to each notification object. Each object gets each transition once.
/// </summary>
public sealed class HyprlandLockNotifier : ILockStateObserver, IDisposable, IHyprlandLockNotifierV1Handler
{
    /// <summary>
    /// The highest protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly ILockState? _state;
    private readonly List<Notification> _notifications = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="HyprlandLockNotifier"/> class and subscribes to <paramref name="state"/>.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="state">The lock state. When it is <see langword="null"/>, the notifier sends events only when the caller calls <see cref="SessionLocked"/> or <see cref="SessionUnlocked"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public HyprlandLockNotifier(WlServerDisplay display, ILockState? state)
    {
        ArgumentNullException.ThrowIfNull(display);
        _state = state;
        _global = display.CreateGlobal(HyprlandLockNotifierV1.Interface, Version, OnBind);
        _state?.AddObserver(this);
    }

    /// <summary>
    /// Gets the number of live <c>hyprland_lock_notification_v1</c> objects.
    /// </summary>
    public int NotificationCount => _notifications.Count;

    /// <summary>
    /// Sends <c>locked</c> to each notification object that is not already in the locked state.
    /// </summary>
    public void SessionLocked()
    {
        for (var i = 0; i < _notifications.Count; i++)
        {
            _notifications[i].Locked();
        }
    }

    /// <summary>
    /// Sends <c>unlocked</c> to each notification object that is in the locked state.
    /// </summary>
    public void SessionUnlocked()
    {
        for (var i = 0; i < _notifications.Count; i++)
        {
            _notifications[i].Unlocked();
        }
    }

    /// <summary>
    /// Unsubscribes from the lock state and destroys the global.
    /// </summary>
    public void Dispose()
    {
        _state?.RemoveObserver(this);
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        _ = new HyprlandLockNotifierV1Resource(client, version, id, this);
    }

    void IHyprlandLockNotifierV1Handler.Destroy(HyprlandLockNotifierV1Resource resource)
    {
    }

    void IHyprlandLockNotifierV1Handler.GetLockNotification(
        HyprlandLockNotifierV1Resource resource,
        in HyprlandLockNotifierV1Resource.GetLockNotificationEventArgs args)
    {
        var notificationResource = new HyprlandLockNotificationV1Resource(resource.Client, resource.Version, args.Id);
        var notification = new Notification(notificationResource);
        _notifications.Add(notification);
        notificationResource.Destroyed += (_, _) => _notifications.Remove(notification);
        if (_state?.IsLocked == true)
        {
            notification.Locked();
        }
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
