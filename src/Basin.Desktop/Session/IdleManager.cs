using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>ext_idle_notifier_v1</c> and <c>zwp_idle_inhibit_manager_v1</c> globals. Idle notifications time out on the loop and reset on activity.
/// </summary>
public sealed class IdleManager : IDisposable, IExtIdleNotifierV1Handler, IZwpIdleInhibitManagerV1Handler
{
    /// <summary>
    /// The version that the idle notifier global advertises.
    /// </summary>
    public const int NotifierVersion = 2;

    /// <summary>
    /// The version that the idle inhibit global advertises.
    /// </summary>
    public const int InhibitVersion = 1;

    private readonly WlGlobal _notifyGlobal;
    private readonly WlGlobal _inhibitGlobal;
    private readonly CompositorGlobal _compositor;
    private readonly ICompositorEventLoop _loop;
    private readonly IIdleSource? _idle;
    private readonly List<Notification> _notifications = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="IdleManager"/> class and creates both globals.
    /// </summary>
    /// <param name="display">The display that holds the globals.</param>
    /// <param name="loop">The loop that holds the notification timers.</param>
    /// <param name="compositor">The compositor global that resolves inhibiting surfaces.</param>
    /// <param name="idle">The source of activity and inhibition, or <see langword="null"/>. Without one, no notification ever becomes idle.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public IdleManager(WlServerDisplay display, ICompositorEventLoop loop, CompositorGlobal compositor, IIdleSource? idle)
    {
        ArgumentNullException.ThrowIfNull(display);
        _loop = loop;
        _compositor = compositor;
        _idle = idle;
        _notifyGlobal = display.CreateGlobal(ExtIdleNotifierV1.Interface, NotifierVersion, OnBindNotifier);
        _inhibitGlobal = display.CreateGlobal(ZwpIdleInhibitManagerV1.Interface, InhibitVersion, OnBindInhibit);
        if (_idle is { } live)
        {
            live.Activity += NotifyActivity;
            live.InhibitionChanged += OnInhibitionChanged;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the idle source reports an inhibitor. It is <see langword="false"/> without a source.
    /// </summary>
    public bool IsInhibited => _idle?.IsInhibited ?? false;

    /// <summary>
    /// Stops following the idle source and removes both globals.
    /// </summary>
    public void Dispose()
    {
        if (_idle is { } live)
        {
            live.Activity -= NotifyActivity;
            live.InhibitionChanged -= OnInhibitionChanged;
        }

        _notifyGlobal.Dispose();
        _inhibitGlobal.Dispose();
    }

    /// <summary>
    /// Resets every idle notification and restarts its timer when an idle source exists. A notification that was idle sends <c>resumed</c>.
    /// </summary>
    public void NotifyActivity()
    {
        foreach (var notification in _notifications)
        {
            notification.OnActivity();
        }
    }

    private void OnInhibitionChanged()
    {
        if (IsInhibited)
        {
            return;
        }

        foreach (var notification in _notifications)
        {
            notification.OnInhibitionReleased();
        }
    }

    private void OnBindNotifier(WlClient client, uint version, uint id) =>
        _ = new ExtIdleNotifierV1Resource(client, version, id, this);

    void IExtIdleNotifierV1Handler.Destroy(ExtIdleNotifierV1Resource resource)
    {
    }

    void IExtIdleNotifierV1Handler.GetIdleNotification(
        ExtIdleNotifierV1Resource resource,
        in ExtIdleNotifierV1Resource.GetIdleNotificationEventArgs args) =>
        Create(resource, args.Id, args.Timeout, ignoresInhibitors: false);

    void IExtIdleNotifierV1Handler.GetInputIdleNotification(
        ExtIdleNotifierV1Resource resource,
        in ExtIdleNotifierV1Resource.GetInputIdleNotificationEventArgs args) =>
        Create(resource, args.Id, args.Timeout, ignoresInhibitors: true);

    private void Create(ExtIdleNotifierV1Resource notifier, uint id, uint timeout, bool ignoresInhibitors)
    {
        var resource = new ExtIdleNotificationV1Resource(notifier.Client, notifier.Version, id);
        var notification = new Notification(this, resource, timeout, ignoresInhibitors);
        _notifications.Add(notification);
        resource.Destroyed += (_, _) =>
        {
            _notifications.Remove(notification);
            notification.Dispose();
        };
    }

    private void OnBindInhibit(WlClient client, uint version, uint id) =>
        _ = new ZwpIdleInhibitManagerV1Resource(client, version, id, this);

    void IZwpIdleInhibitManagerV1Handler.Destroy(ZwpIdleInhibitManagerV1Resource resource)
    {
    }

    void IZwpIdleInhibitManagerV1Handler.CreateInhibitor(
        ZwpIdleInhibitManagerV1Resource manager,
        in ZwpIdleInhibitManagerV1Resource.CreateInhibitorEventArgs args)
    {
        var resource = new ZwpIdleInhibitorV1Resource(manager.Client, manager.Version, args.Id);

        var held = _idle?.Inhibit();
        resource.Destroyed += (_, _) => held?.Dispose();
    }

    private sealed class Notification : IDisposable
    {
        private readonly IdleManager _owner;
        private readonly ExtIdleNotificationV1Resource _resource;
        private readonly IEventSource _timer;
        private readonly uint _timeoutMs;
        private readonly bool _ignoresInhibitors;
        private bool _idle;
        private bool _elapsed;

        public Notification(
            IdleManager owner,
            ExtIdleNotificationV1Resource resource,
            uint timeoutMs,
            bool ignoresInhibitors)
        {
            _owner = owner;
            _resource = resource;
            _timeoutMs = Math.Max(1, timeoutMs);
            _ignoresInhibitors = ignoresInhibitors;
            _timer = owner._loop.AddTimer(OnTimeout);

            if (owner._idle is not null)
            {
                _timer.UpdateTimer((int)_timeoutMs);
            }
        }

        public void OnActivity()
        {
            _elapsed = false;
            if (_idle)
            {
                _idle = false;
                if (!_resource.IsDestroyed)
                {
                    _resource.SendResumed();
                }
            }

            if (_owner._idle is not null)
            {
                _timer.UpdateTimer((int)_timeoutMs);
            }
        }

        public void OnInhibitionReleased()
        {
            if (_elapsed)
            {
                Idle();
            }
        }

        private void OnTimeout()
        {
            _elapsed = true;
            Idle();
        }

        private void Idle()
        {
            if (_idle || _resource.IsDestroyed || (!_ignoresInhibitors && _owner.IsInhibited))
            {
                return;
            }

            _idle = true;
            _resource.SendIdled();
        }

        public void Dispose() => _timer.Remove();
    }
}
