using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class IdleManager : IDisposable
{
    public const int NotifierVersion = 2;

    public const int InhibitVersion = 1;

    private readonly WlGlobal _notifyGlobal;
    private readonly WlGlobal _inhibitGlobal;
    private readonly CompositorGlobal _compositor;
    private readonly ICompositorEventLoop _loop;
    private readonly IIdleSource? _idle;
    private readonly List<Notification> _notifications = [];

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

    public bool IsInhibited => _idle?.IsInhibited ?? false;

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

    private void OnBindNotifier(WlClient client, uint version, uint id)
    {
        var notifier = new ExtIdleNotifierV1Resource(client, version, id);
        notifier.GetIdleNotification += (_, e) => Create(notifier, e.Id, e.Timeout, ignoresInhibitors: false);
        notifier.GetInputIdleNotification += (_, e) => Create(notifier, e.Id, e.Timeout, ignoresInhibitors: true);
    }

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

    private void OnBindInhibit(WlClient client, uint version, uint id)
    {
        var manager = new ZwpIdleInhibitManagerV1Resource(client, version, id);
        manager.CreateInhibitor += (_, e) =>
        {
            var resource = new ZwpIdleInhibitorV1Resource(client, manager.Version, e.Id);

            var held = _idle?.Inhibit();
            resource.Destroyed += (_, _) => held?.Dispose();
        };
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
