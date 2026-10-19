using Basin.Capabilities;

namespace Basin.Host;

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

    public long UIHorizonMillis { get; set; } = 50;

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

    public int QuietPumps { get; set; } = 2;

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
