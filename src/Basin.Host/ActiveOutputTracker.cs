namespace Basin.Host;

/// <summary>
/// Tracks the output that the pointer is on, so that new windows and chrome can open there. The owner reports pointer motion and output changes.
/// </summary>
public sealed class ActiveOutputTracker
{
    private readonly OutputLayout _layout;
    private readonly List<IOutput> _outputs = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveOutputTracker"/> class.
    /// </summary>
    /// <param name="layout">The layout that maps a pointer position to an output.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    public ActiveOutputTracker(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    /// <summary>
    /// Gets the active output. It is <see langword="null"/> before the first <see cref="Added"/> call and after the last output is removed.
    /// </summary>
    public IOutput? Current { get; private set; }

    /// <summary>
    /// Occurs when <see cref="Moved"/> puts the pointer on a different output. <see cref="Added"/> and <see cref="Removed"/> change <see cref="Current"/> without this event.
    /// </summary>
    public event Action<IOutput>? Changed;

    /// <summary>
    /// Reports a pointer position. When the position is on an output that is not <see cref="Current"/>, that output becomes current. A position outside all outputs changes nothing.
    /// </summary>
    /// <param name="x">The pointer X position in layout coordinates.</param>
    /// <param name="y">The pointer Y position in layout coordinates.</param>
    public void Moved(double x, double y)
    {
        if (_layout.OutputAt(x, y) is { } output && !ReferenceEquals(output, Current))
        {
            Current = output;
            Changed?.Invoke(output);
        }
    }

    /// <summary>
    /// Reports a new output. The output becomes <see cref="Current"/> when no output is current.
    /// </summary>
    /// <param name="output">The output that was added.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public void Added(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!_outputs.Contains(output))
        {
            _outputs.Add(output);
        }

        Current ??= output;
    }

    /// <summary>
    /// Reports a removed output. When it was <see cref="Current"/>, the output under the pointer becomes current, otherwise the oldest remaining output.
    /// </summary>
    /// <param name="output">The output that was removed.</param>
    /// <param name="pointerX">The pointer X position in layout coordinates.</param>
    /// <param name="pointerY">The pointer Y position in layout coordinates.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public void Removed(IOutput output, double pointerX, double pointerY)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputs.Remove(output);
        if (!ReferenceEquals(Current, output))
        {
            return;
        }

        Current = _layout.OutputAt(pointerX, pointerY) is { } under && !ReferenceEquals(under, output)
            ? under
            : _outputs.FirstOrDefault();
    }
}
