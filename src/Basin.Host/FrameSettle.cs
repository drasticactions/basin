using Basin.Capabilities;

namespace Basin.Host;

/// <summary>
/// Tells when the outputs and the UI toolkits have no more work, so that a screenshot or a handoff can wait for a stable frame.
/// </summary>
public sealed class FrameSettle : IFrameSettle, IDisposable
{
    private readonly OutputDriver _outputs;
    private readonly List<UIDriver> _ui = [];
    private readonly Action _check;
    private readonly Action<OutputView> _watch;
    private readonly Action<OutputView> _unwatch;
    private readonly Action _woken;
    private Action? _settled;
    private int _quietPumps;
    private long _presentedAtPump = -1;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameSettle"/> class and watches the schedulers of the current and later outputs.
    /// </summary>
    /// <param name="outputs">The driver whose outputs are watched.</param>
    /// <param name="ui">A UI driver to watch, or <see langword="null"/>. <see cref="Add"/> adds more.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outputs"/> is <see langword="null"/>.</exception>
    public FrameSettle(OutputDriver outputs, UIDriver? ui = null)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        _outputs = outputs;
        _check = Check;
        _watch = Watch;
        _unwatch = Unwatch;
        _woken = OnWoken;
        outputs.Added += _watch;
        outputs.Removed += _unwatch;
        foreach (var view in outputs.Views)
        {
            Watch(view);
        }

        if (ui is not null)
        {
            Add(ui);
        }
    }

    /// <summary>
    /// Gets or sets the time in milliseconds within which due UI work keeps the frame unsettled. Work due later does not count.
    /// </summary>
    public long UIHorizonMillis { get; set; } = 50;

    /// <summary>
    /// Gets a value indicating whether every output scheduler is idle and no UI driver has work within <see cref="UIHorizonMillis"/>.
    /// </summary>
    public bool IsSettled
    {
        get
        {
            foreach (var view in _outputs.Views)
            {
                if (view.Scheduler is { IsIdle: false })
                {
                    return false;
                }
            }

            for (var i = 0; i < _ui.Count; i++)
            {
                if (_ui[i].NextDueMillis is { } due && due <= UIHorizonMillis)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Gets or sets the number of timer pumps with idle outputs and no new frames after which the frame counts as settled. This handles a toolkit that always has a timer due.
    /// </summary>
    public int QuietPumps { get; set; } = 2;

    /// <summary>
    /// Gets the sum of the committed frames of all outputs.
    /// </summary>
    public long Presented
    {
        get
        {
            var presented = 0L;
            foreach (var view in _outputs.Views)
            {
                presented += view.Rendered;
            }

            return presented;
        }
    }

    /// <summary>
    /// Occurs each time a check finds the frame settled, or after <see cref="QuietPumps"/> quiet pumps. It can occur more than once. Adding a handler resets the quiet pump count.
    /// </summary>
    public event Action? Settled
    {
        add
        {
            _settled += value;
            _quietPumps = 0;
            _presentedAtPump = -1;
        }

        remove => _settled -= value;
    }

    /// <summary>
    /// Watches a UI driver. A driver that is already watched is ignored.
    /// </summary>
    /// <param name="ui">The UI driver to watch.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ui"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void Add(UIDriver ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ui.Contains(ui))
        {
            return;
        }

        _ui.Add(ui);
        ui.Pumped += _check;
        ui.Woken += _woken;
    }

    /// <summary>
    /// Stops watching a UI driver and checks again whether the frame is settled.
    /// </summary>
    /// <param name="ui">The UI driver to stop watching.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ui"/> is <see langword="null"/>.</exception>
    public void Remove(UIDriver ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (!_ui.Remove(ui))
        {
            return;
        }

        ui.Pumped -= _check;
        ui.Woken -= _woken;
        Check();
    }

    /// <summary>
    /// Stops watching the outputs and the UI drivers. <see cref="Settled"/> does not occur after this call.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _outputs.Added -= _watch;
        _outputs.Removed -= _unwatch;
        foreach (var view in _outputs.Views)
        {
            Unwatch(view);
        }

        foreach (var ui in _ui)
        {
            ui.Pumped -= _check;
            ui.Woken -= _woken;
        }

        _ui.Clear();
    }

    private void Watch(OutputView view)
    {
        if (view.Scheduler is { } scheduler)
        {
            scheduler.Idle += _check;
        }
    }

    private void Unwatch(OutputView view)
    {
        if (view.Scheduler is { } scheduler)
        {
            scheduler.Idle -= _check;
        }

        Check();
    }

    private void OnWoken()
    {
        if (_disposed || _settled is null)
        {
            return;
        }

        var presented = Presented;
        if (!OutputsIdle() || presented != _presentedAtPump)
        {
            _quietPumps = 0;
            _presentedAtPump = presented;
        }
        else
        {
            _quietPumps++;
        }

        if (IsSettled || _quietPumps >= QuietPumps)
        {
            _settled();
        }
    }

    private bool OutputsIdle()
    {
        foreach (var view in _outputs.Views)
        {
            if (view.Scheduler is { IsIdle: false })
            {
                return false;
            }
        }

        return true;
    }

    private void Check()
    {
        if (!_disposed && _settled is { } settled && IsSettled)
        {
            settled();
        }
    }
}
