using Basin.Capabilities;

namespace Basin.Shell.Nested;

/// <summary>
/// An <see cref="IUIScreenSource"/> with the one screen of the shell, keyed by <see cref="NestedShell.OutputKey"/>.
/// </summary>
public sealed class ShellScreenSource : IUIScreenSource
{
    private Box _output;
    private double _scale;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellScreenSource"/> class.
    /// </summary>
    /// <param name="output">The output box in logical layout coordinates.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="transform">The output transform.</param>
    public ShellScreenSource(Box output, double scale, OutputTransform transform = OutputTransform.Normal)
    {
        _output = output;
        _scale = scale;
        Transform = transform;
    }

    /// <summary>
    /// Gets the output transform that the last <see cref="Update"/> applied.
    /// </summary>
    public OutputTransform Transform { get; private set; }

    /// <summary>
    /// Gets the number of screens. It is always 1.
    /// </summary>
    public int Count => 1;

    /// <summary>
    /// Occurs when <see cref="Update"/> changes the box, the scale or the transform.
    /// </summary>
    public event Action? Changed;

    /// <inheritdoc/>
    public bool TryGet(int index, out UIScreenInfo info)
    {
        info = new UIScreenInfo(NestedShell.OutputKey, _output.X, _output.Y, _output.Width, _output.Height, _scale, true);
        return index == 0;
    }

    /// <summary>
    /// Replaces the screen and raises <see cref="Changed"/>. It does nothing when no value changes.
    /// </summary>
    /// <param name="output">The output box in logical layout coordinates.</param>
    /// <param name="scale">The output scale.</param>
    /// <param name="transform">The output transform.</param>
    public void Update(Box output, double scale, OutputTransform transform = OutputTransform.Normal)
    {
        if (output == _output && Math.Abs(scale - _scale) < double.Epsilon && transform == Transform)
        {
            return;
        }

        _output = output;
        _scale = scale;
        Transform = transform;
        Changed?.Invoke();
    }
}
