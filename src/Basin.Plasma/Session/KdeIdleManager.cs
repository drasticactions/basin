using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class KdeIdleManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly ICompositorEventLoop _loop;
    private readonly IIdleSource? _idle;
    private readonly List<Timeout> _timeouts = [];

    public KdeIdleManager(WlServerDisplay display, ICompositorEventLoop loop, IIdleSource? idle)
    {
        ArgumentNullException.ThrowIfNull(display);
        _loop = loop;
        _idle = idle;
        _global = display.CreateGlobal(OrgKdeKwinIdle.Interface, Version, OnBind);
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

        _global.Dispose();
    }

    public void NotifyActivity()
    {
        foreach (var timeout in _timeouts)
        {
            timeout.OnActivity();
        }
    }

    private void OnInhibitionChanged()
    {
        foreach (var timeout in _timeouts)
        {
            timeout.OnInhibitionChanged();
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new OrgKdeKwinIdleResource(client, version, id);
        manager.GetIdleTimeout += (_, e) =>
        {
            var resource = new OrgKdeKwinIdleTimeoutResource(client, manager.Version, e.Id);
            resource.SimulateUserActivity += (_, _) => _idle?.NotifyActivity();
            var timeout = new Timeout(this, resource, e.Timeout);
            _timeouts.Add(timeout);
            resource.Destroyed += (_, _) =>
            {
                _timeouts.Remove(timeout);
                timeout.Dispose();
            };
        };
    }

    private sealed class Timeout : IDisposable
    {
        private readonly KdeIdleManager _owner;
        private readonly OrgKdeKwinIdleTimeoutResource _resource;
        private readonly IEventSource _timer;
        private readonly uint _timeoutMs;
        private bool _idle;
        private bool _passQueued;

        public Timeout(KdeIdleManager owner, OrgKdeKwinIdleTimeoutResource resource, uint timeoutMs)
        {
            _owner = owner;
            _resource = resource;
            _timeoutMs = timeoutMs;
            _timer = owner._loop.AddTimer(OnTimeout);
            Arm();
        }

        public void OnActivity()
        {
            if (_idle)
            {
                _idle = false;
                if (!_resource.IsDestroyed)
                {
                    _resource.SendResumed();
                }
            }

            Arm();
        }

        public void OnInhibitionChanged()
        {
            if (!_owner.IsInhibited)
            {
                Arm();
                return;
            }

            _timer.UpdateTimer(0);
            if (_idle)
            {
                _idle = false;
                if (!_resource.IsDestroyed)
                {
                    _resource.SendResumed();
                }
            }
        }

        private void Arm()
        {
            if (_owner._idle is null || _owner.IsInhibited)
            {
                return;
            }

            if (_timeoutMs == 0)
            {
                if (!_passQueued)
                {
                    _passQueued = true;
                    _owner._loop.AddIdle(OnPass);
                }

                return;
            }

            _timer.UpdateTimer((int)_timeoutMs);
        }

        private void OnPass()
        {
            _passQueued = false;
            if (_timer.IsRemoved)
            {
                return;
            }

            OnTimeout();
        }

        private void OnTimeout()
        {
            if (_idle || _resource.IsDestroyed || _owner.IsInhibited)
            {
                return;
            }

            _idle = true;
            _resource.SendIdle();
        }

        public void Dispose() => _timer.Remove();
    }
}
