namespace Basin.WindowManager;

public sealed class ManageContext
{
    private readonly RiverWindowManager _wm;
    private bool _alive;

    internal ManageContext(RiverWindowManager wm, RenderContext render)
    {
        _wm = wm;
        Render = render;
    }

    public IReadOnlyList<WmWindow> Windows
    {
        get
        {
            EnsureAlive();
            return _wm.Windows;
        }
    }

    public IReadOnlyList<WmWindow> NewWindows
    {
        get
        {
            EnsureAlive();
            return _wm.NewWindows;
        }
    }

    public IReadOnlyList<WmWindow> ClosedWindows
    {
        get
        {
            EnsureAlive();
            return _wm.ClosedWindows;
        }
    }

    public IReadOnlyList<WmOutput> Outputs
    {
        get
        {
            EnsureAlive();
            return _wm.Outputs;
        }
    }

    public IReadOnlyList<WmSeat> Seats
    {
        get
        {
            EnsureAlive();
            return _wm.Seats;
        }
    }

    public bool SessionIsLocked
    {
        get
        {
            EnsureAlive();
            return _wm.SessionIsLocked;
        }
    }

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
