namespace Basin.Host;

/// <summary>
/// Shows the time in shell chrome and updates it on the loop at the next minute or second boundary.
/// </summary>
public sealed class ShellClock : IDisposable
{
    private readonly IEventSource _timer;
    private readonly Func<DateTime, string?> _format;
    private readonly Action<string?> _show;
    private string? _frozen;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellClock"/> class. The clock shows nothing until the first <see cref="Refresh"/>.
    /// </summary>
    /// <param name="loop">The loop that runs the timer.</param>
    /// <param name="format">The function that formats the local time for display.</param>
    /// <param name="show">The function that puts the text into the chrome.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ShellClock(ICompositorEventLoop loop, Func<DateTime, string?> format, Action<string?> show)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(show);
        _format = format;
        _show = show;
        _timer = loop.AddTimer(Refresh);
    }

    /// <summary>
    /// Gets or sets the function that tells whether the text shows seconds. When it returns <see langword="true"/>, the clock updates every second, otherwise every minute.
    /// </summary>
    public Func<bool>? PerSecond { get; set; }

    /// <summary>
    /// Gets or sets a fixed text that the clock shows instead of the time, for example in a golden test. While it is set, the timer stops. Setting it refreshes the clock at once.
    /// </summary>
    public string? Frozen
    {
        get => _frozen;
        set
        {
            _frozen = value;
            Refresh();
        }
    }

    /// <summary>
    /// Shows the current text and sets the timer for the next update. It does nothing after <see cref="Dispose"/>.
    /// </summary>
    public void Refresh()
    {
        if (_timer.IsRemoved)
        {
            return;
        }

        if (_frozen is { } frozen)
        {
            _show(frozen);
            return;
        }

        var now = DateTime.Now;
        _show(_format(now));
        var delay = PerSecond?.Invoke() == true
            ? 1000 - now.Millisecond
            : ((60 - now.Second) * 1000) - now.Millisecond;
        _timer.UpdateTimer(Math.Max(1, delay));
    }

    /// <summary>
    /// Removes the timer. The clock shows nothing more after this call.
    /// </summary>
    public void Dispose()
    {
        if (!_timer.IsRemoved)
        {
            _timer.Remove();
        }
    }
}
