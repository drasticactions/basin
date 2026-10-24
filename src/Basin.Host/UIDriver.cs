using Basin.Capabilities;
using Basin.Scene;

namespace Basin.Host;

/// <summary>
/// Runs a UI toolkit host on the compositor loop. It pumps the host when its next work is due, and puts the toolkit's popups into the scene.
/// </summary>
public sealed class UIDriver : IDisposable
{
    private readonly IUIHost _host;
    private readonly ICompositorEventLoop _loop;
    private readonly List<UISurfaceNode> _popups = [];
    private IEventSource? _timer;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UIDriver"/> class. Nothing runs until <see cref="Start"/>.
    /// </summary>
    /// <param name="host">The toolkit host to pump. The driver does not own it.</param>
    /// <param name="loop">The loop that runs the pump timer.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public UIDriver(IUIHost host, ICompositorEventLoop loop)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(loop);

        _host = host;
        _loop = loop;
    }

    /// <summary>
    /// Gets or sets the tree that new popups go into. While it is <see langword="null"/>, the driver ignores new popups.
    /// </summary>
    public SceneTree? PopupLayer { get; set; }

    /// <summary>
    /// Gets or sets the index that maps popup nodes back to their surfaces for input, or <see langword="null"/> for none.
    /// </summary>
    public UISurfaceIndex? Index { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether new popup nodes damage only the changed regions instead of the whole node.
    /// </summary>
    public bool PreciseDamage { get; set; } = true;

    /// <summary>
    /// Gets the scene nodes of the open popups.
    /// </summary>
    public IReadOnlyList<UISurfaceNode> Popups => _popups;

    /// <summary>
    /// Gets the time in milliseconds until the host has work, or <see langword="null"/> when it has none.
    /// </summary>
    public long? NextDueMillis => _host.NextDueMillis;

    /// <summary>
    /// Occurs after the timer pumps the host. A direct <see cref="Pump"/> call does not raise it.
    /// </summary>
    public event Action? Woken;

    /// <summary>
    /// Occurs after each pump, when the popups are in position and the timer is set again.
    /// </summary>
    public event Action? Pumped;

    /// <summary>
    /// Occurs when the driver puts a new popup into <see cref="PopupLayer"/>.
    /// </summary>
    public event Action<IUISurface>? PopupAdded;

    /// <summary>
    /// Occurs when the toolkit dismisses a popup. The driver removes the popup's node before it raises the event.
    /// </summary>
    public event Action<IUISurface>? PopupRemoved;

    /// <summary>
    /// Subscribes to the host's wakeups and popups, and sets the timer. A second call or a call after <see cref="Dispose"/> does nothing.
    /// </summary>
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

    /// <summary>
    /// Pumps the host now, moves the popups to their surfaces' positions, and sets the timer again. It does nothing after <see cref="Dispose"/>. When the host draws on a lost device, the pump stops there, and the owner of the host replaces it after the renderer rebuild.
    /// </summary>
    public void Pump()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _host.Pump();
        }
        catch (RendererLostException)
        {
            return;
        }

        SyncPopups();
        Schedule();
        Pumped?.Invoke();
    }

    /// <summary>
    /// Moves each popup node to its surface's position, rounded to whole pixels, and copies whether the surface accepts input.
    /// </summary>
    public void SyncPopups()
    {
        foreach (var popup in _popups)
        {
            if (popup.Surface is { } surface)
            {
                popup.SetPosition((int)Math.Round(surface.PositionX), (int)Math.Round(surface.PositionY));
                popup.InputEnabled = surface.AcceptsInput;
            }
        }
    }

    /// <summary>
    /// Sets the timer to the host's next due time, at least 1 ms, or stops the timer when the host has no work.
    /// </summary>
    public void Schedule()
    {
        if (_disposed || _timer is null || _timer.IsRemoved)
        {
            return;
        }

        var due = _host.NextDueMillis;
        _timer.UpdateTimer(due is null ? -1 : (int)Math.Clamp(due.Value, 1, int.MaxValue));
    }

    /// <summary>
    /// Unsubscribes from the host, destroys the popup nodes and removes the timer. It does not dispose the host.
    /// </summary>
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
        node.InputEnabled = popup.AcceptsInput;
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
