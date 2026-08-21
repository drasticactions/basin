using Basin.Capabilities;
using Basin.Scene;

namespace Basin.Host;

public sealed class UIDriver : IDisposable
{
    private readonly IUIHost _host;
    private readonly ICompositorEventLoop _loop;
    private readonly List<UISurfaceNode> _popups = [];
    private IEventSource? _timer;
    private bool _started;
    private bool _disposed;

    public UIDriver(IUIHost host, ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(loop);

        _host = host;
        _loop = loop;
    }

    public SceneTree? PopupLayer { get; set; }

    public UISurfaceIndex? Index { get; set; }

    public bool PreciseDamage { get; set; } = true;

    public IReadOnlyList<UISurfaceNode> Popups => _popups;

    public event Action? Woken;

    public event Action<IUISurface>? PopupAdded;

    public event Action<IUISurface>? PopupRemoved;

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _timer = _loop.AddTimer(OnTimer);
        _host.WakeupRequested += Schedule;
        _host.PopupAppeared += OnPopupAppeared;
        _host.PopupDismissed += OnPopupDismissed;
        Schedule();
    }

    public void Pump()
    {
        if (_disposed)
        {
            return;
        }

        _host.Pump();
        SyncPopups();
        Schedule();
    }

    public void SyncPopups()
    {
        foreach (var popup in _popups)
        {
            if (popup.Surface is { } surface)
            {
                popup.SetPosition((int)Math.Round(surface.PositionX), (int)Math.Round(surface.PositionY));
            }
        }
    }

    public void Schedule()
    {
        if (_disposed || _timer is null || _timer.IsRemoved)
        {
            return;
        }

        var due = _host.NextDueMillis;
        _timer.UpdateTimer(due is null ? -1 : (int)Math.Clamp(due.Value, 0, int.MaxValue));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started)
        {
            _host.WakeupRequested -= Schedule;
            _host.PopupAppeared -= OnPopupAppeared;
            _host.PopupDismissed -= OnPopupDismissed;
        }

        foreach (var popup in _popups)
        {
            popup.Dispose();
        }

        _popups.Clear();
        _timer?.Remove();
        _timer = null;
    }

    private void OnTimer()
    {
        Pump();
        Woken?.Invoke();
    }

    private void OnPopupAppeared(IUISurface popup)
    {
        if (_disposed || PopupLayer is not { } layer)
        {
            return;
        }

        var node = new UISurfaceNode(layer, popup, Index) { PreciseDamage = PreciseDamage };
        node.SetPosition((int)Math.Round(popup.PositionX), (int)Math.Round(popup.PositionY));
        node.Publish();
        _popups.Add(node);
        PopupAdded?.Invoke(popup);
    }

    private void OnPopupDismissed(IUISurface popup)
    {
        for (var i = _popups.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_popups[i].Surface, popup))
            {
                _popups[i].Dispose();
                _popups.RemoveAt(i);
            }
        }

        PopupRemoved?.Invoke(popup);
    }
}
