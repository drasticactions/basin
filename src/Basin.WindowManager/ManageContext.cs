namespace Basin.WindowManager;

/// <summary>
/// The view of the compositor that a manage callback gets. It is valid only while the callback runs. A later use throws <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class ManageContext
{
    private readonly RiverWindowManager _wm;
    private bool _alive;

    internal ManageContext(RiverWindowManager wm, RenderContext render)
    {
        _wm = wm;
        Render = render;
    }

    /// <summary>
    /// Gets every open window, in the order the compositor announced them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The manage sequence of this context has finished.</exception>
    public IReadOnlyList<WmWindow> Windows
    {
        get
        {
            EnsureAlive();
            return _wm.Windows;
        }
    }

    /// <summary>
    /// Gets the windows that the compositor announced since the previous manage sequence. A manager subscribes to a new window's events here.
    /// </summary>
    /// <exception cref="InvalidOperationException">The manage sequence of this context has finished.</exception>
    public IReadOnlyList<WmWindow> NewWindows
    {
        get
        {
            EnsureAlive();
            return _wm.NewWindows;
        }
    }

    /// <summary>
    /// Gets the windows that closed since the previous manage sequence. The library destroys their objects when the callback returns.
    /// </summary>
    /// <exception cref="InvalidOperationException">The manage sequence of this context has finished.</exception>
    public IReadOnlyList<WmWindow> ClosedWindows
    {
        get
        {
            EnsureAlive();
            return _wm.ClosedWindows;
        }
    }

    /// <summary>
    /// Gets the outputs that are present. A removed output leaves this list before its <see cref="WmOutput.Removed"/> event fires.
    /// </summary>
    /// <exception cref="InvalidOperationException">The manage sequence of this context has finished.</exception>
    public IReadOnlyList<WmOutput> Outputs
    {
        get
        {
            EnsureAlive();
            return _wm.Outputs;
        }
    }

    /// <summary>
    /// Gets the seats that are present. A removed seat leaves this list before its <see cref="WmSeat.Removed"/> event fires.
    /// </summary>
    /// <exception cref="InvalidOperationException">The manage sequence of this context has finished.</exception>
    public IReadOnlyList<WmSeat> Seats
    {
        get
        {
            EnsureAlive();
            return _wm.Seats;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the session is locked, as of the start of this sequence.
    /// </summary>
    /// <exception cref="InvalidOperationException">The manage sequence of this context has finished.</exception>
    public bool SessionIsLocked
    {
        get
        {
            EnsureAlive();
            return _wm.SessionIsLocked;
        }
    }

    /// <summary>
    /// Gets the rendering view, which is also valid during a manage sequence. Rendering requests made here apply with the next render finish.
    /// </summary>
    public RenderContext Render { get; }

    internal void Revive() => _alive = true;

    internal void Kill() => _alive = false;

    private void EnsureAlive()
    {
        WmThreadAffinity.Assert();
        if (!_alive)
        {
            throw new InvalidOperationException(
                "this ManageContext belongs to a manage sequence that has already finished");
        }
    }
}
