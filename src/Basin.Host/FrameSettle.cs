using System.Text;
using Basin.Capabilities;

namespace Basin.Host;

/// <summary>
/// Tells when the outputs, the UI toolkits, the windows and the animations have no more work, so that a screenshot or a handoff can wait for a stable frame.
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
    private IToplevelModel? _toplevels;
    private IAnimationClock? _animationClock;
    private ToplevelInfo[] _infos = new ToplevelInfo[16];
    private IEventSource? _recheck;

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
    /// Gets a value indicating whether every output scheduler is idle, no UI driver has work within <see cref="UIHorizonMillis"/>, every mapped window answered its newest configure and no animation asked for another frame.
    /// </summary>
    public bool IsSettled => OutputsIdle() && UIQuiet() && ClientsSettled();

    /// <summary>
    /// Gets or sets the windows whose configures must be answered. With <see langword="null"/>, the settle uses the <see cref="IToplevelModel"/> of the host services.
    /// </summary>
    public IToplevelModel? Toplevels
    {
        get => _toplevels ?? _outputs.Host.Services?.Find<IToplevelModel>();
        set => _toplevels = value;
    }

    /// <summary>
    /// Gets or sets the clock whose running animations keep the frame unsettled. With <see langword="null"/>, the settle uses the <see cref="IAnimationClock"/> of the host services.
    /// </summary>
    public IAnimationClock? AnimationClock
    {
        get => _animationClock ?? _outputs.Host.Services?.Find<IAnimationClock>();
        set => _animationClock = value;
    }

    /// <summary>
    /// Gets or sets the interval in milliseconds at which a waiting settle checks the windows and the animations again. No output event tells when a client answers a configure.
    /// </summary>
    public int RecheckMillis { get; set; } = 20;

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
    /// Occurs each time a check finds the frame settled, or after <see cref="QuietPumps"/> quiet pumps. It can occur more than once. Adding a handler resets the quiet pump count and checks again every <see cref="RecheckMillis"/> until the frame settles.
    /// </summary>
    public event Action? Settled
    {
        add
        {
            _settled += value;
            _quietPumps = 0;
            _presentedAtPump = -1;
            ArmRecheck();
        }

        remove
        {
            _settled -= value;
            if (_settled is null)
            {
                _recheck?.UpdateTimer(0);
            }
        }
    }

    /// <summary>
    /// Describes what keeps the frame unsettled: each output with a repaint pending, each UI driver with work due, each window that did not answer its newest configure, and a running animation.
    /// </summary>
    /// <returns>One sentence for each reason, or an empty string when the frame is settled.</returns>
    public string DescribeUnsettled()
    {
        var text = new StringBuilder();
        foreach (var view in _outputs.Views)
        {
            if (view.Scheduler is { IsIdle: false })
            {
                Sentence(text, $"Output {view.Output.Name} has a repaint pending.");
            }
        }

        for (var i = 0; i < _ui.Count; i++)
        {
            if (_ui[i].NextDueMillis is { } due && due <= UIHorizonMillis)
            {
                Sentence(text, $"A UI toolkit has work due in {due} ms.");
            }
        }

        if (Toplevels is { } toplevels)
        {
            var count = Snapshot(toplevels);
            for (var i = 0; i < count; i++)
            {
                var info = _infos[i];
                if (toplevels.IsConfigurePending(info.Id))
                {
                    Sentence(text, $"Window {info.Id} with app id \"{info.AppId}\" and title \"{info.Title}\" did not answer its newest configure.");
                }
            }

            Array.Clear(_infos, 0, count);
        }

        if (AnimationClock is { IsAnimating: true })
        {
            Sentence(text, "An animation is running.");
        }

        return text.ToString();
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
        _recheck?.Remove();
        _recheck = null;
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

        if (OutputsIdle() && ClientsSettled() && (UIQuiet() || _quietPumps >= QuietPumps))
        {
            Fire();
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

    private bool UIQuiet()
    {
        for (var i = 0; i < _ui.Count; i++)
        {
            if (_ui[i].NextDueMillis is { } due && due <= UIHorizonMillis)
            {
                return false;
            }
        }

        return true;
    }

    private bool ClientsSettled()
    {
        if (AnimationClock is { IsAnimating: true })
        {
            return false;
        }

        if (Toplevels is not { } toplevels)
        {
            return true;
        }

        var count = Snapshot(toplevels);
        var settled = true;
        for (var i = 0; i < count; i++)
        {
            if (toplevels.IsConfigurePending(_infos[i].Id))
            {
                settled = false;
                break;
            }
        }

        Array.Clear(_infos, 0, count);
        return settled;
    }

    private int Snapshot(IToplevelModel toplevels)
    {
        while (true)
        {
            var count = toplevels.Enumerate(_infos);
            if (count >= 0)
            {
                return count;
            }

            _infos = new ToplevelInfo[_infos.Length * 2];
        }
    }

    private static void Sentence(StringBuilder text, string sentence)
    {
        if (text.Length > 0)
        {
            text.Append(' ');
        }

        text.Append(sentence);
    }

    private void ArmRecheck()
    {
        if (_disposed || _settled is null || RecheckMillis <= 0)
        {
            return;
        }

        _recheck ??= _outputs.Host.Loop.AddTimer(OnRecheck);
        _recheck.UpdateTimer(RecheckMillis);
    }

    private void OnRecheck()
    {
        if (_disposed || _settled is null)
        {
            return;
        }

        if (IsSettled)
        {
            Fire();
            return;
        }

        ArmRecheck();
    }

    private void Fire()
    {
        _recheck?.UpdateTimer(0);
        _settled?.Invoke();
    }

    private void Check()
    {
        if (!_disposed && _settled is not null && IsSettled)
        {
            Fire();
        }
    }
}
