namespace Basin.WindowManager;

public sealed class RenderContext
{
    private readonly RiverWindowManager _wm;
    private readonly List<IRenderWindow> _windows = [];
    private bool _alive;

    internal RenderContext(RiverWindowManager wm) => _wm = wm;

    public IReadOnlyList<IRenderWindow> Windows
    {
        get
        {
            EnsureAlive();
            _windows.Clear();
            foreach (var window in _wm.Windows)
            {
                _windows.Add(window);
            }

            return _windows;
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

    public void PlaceTop(WmNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        EnsureAlive();
        node.PlaceTop();
    }

    public void PlaceBottom(WmNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        EnsureAlive();
        node.PlaceBottom();
    }

    internal void Revive() => _alive = true;

    internal void Kill()
    {
        _alive = false;
        _windows.Clear();
    }

    private void EnsureAlive()
    {
        WmThreadAffinity.Assert();
        if (!_alive)
        {
            throw new InvalidOperationException(
                "this RenderContext belongs to a sequence that has already finished");
        }
    }
}
