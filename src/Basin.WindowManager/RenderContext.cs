namespace Basin.WindowManager;

/// <summary>
/// The view of the compositor that a render callback gets, and that a manage callback reaches through <see cref="ManageContext.Render"/>. It is valid only while its callback runs.
/// </summary>
public sealed class RenderContext
{
    private readonly RiverWindowManager _wm;
    private readonly List<IRenderWindow> _windows = [];
    private bool _alive;

    internal RenderContext(RiverWindowManager wm) => _wm = wm;

    /// <summary>
    /// Gets every open window through its rendering members only. The list is reused: each read refills it, and it is empty after the sequence.
    /// </summary>
    /// <exception cref="InvalidOperationException">The sequence of this context has finished.</exception>
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

    /// <summary>
    /// Gets the outputs that are present.
    /// </summary>
    /// <exception cref="InvalidOperationException">The sequence of this context has finished.</exception>
    public IReadOnlyList<WmOutput> Outputs
    {
        get
        {
            EnsureAlive();
            return _wm.Outputs;
        }
    }

    /// <summary>
    /// Places a node above all other nodes in the render list.
    /// </summary>
    /// <param name="node">The node to raise.</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The sequence of this context has finished.</exception>
    public void PlaceTop(WmNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        EnsureAlive();
        node.PlaceTop();
    }

    /// <summary>
    /// Places a node below all other nodes in the render list.
    /// </summary>
    /// <param name="node">The node to lower.</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The sequence of this context has finished.</exception>
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
                "This RenderContext belongs to a sequence that has finished. Use the context only inside its sequence.");
        }
    }
}
