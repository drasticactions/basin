namespace Basin;

/// <summary>
/// Sends <c>wl_surface.enter</c> and <c>leave</c> from surface boxes and output positions, and announces the preferred scale of each surface.
/// </summary>
public sealed class SurfacePresenceTracker
{
    private readonly OutputLayout _layout;
    private readonly Action<Surface, double> _announceScale;
    private readonly List<(IOutput Output, OutputGlobal Global)> _outputs = [];

    /// <summary>Initializes a new instance of the <see cref="SurfacePresenceTracker"/> class.</summary>
    /// <param name="layout">The layout that places each output.</param>
    /// <param name="announceScale">Called for each surface that touches an output, with the largest scale of those outputs.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> or <paramref name="announceScale"/> is <see langword="null"/>.</exception>
    public SurfacePresenceTracker(OutputLayout layout, Action<Surface, double> announceScale)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(announceScale);
        _layout = layout;
        _announceScale = announceScale;
    }

    /// <summary>Adds an output to the overlap test.</summary>
    /// <param name="output">The output.</param>
    /// <param name="global">The output's <c>wl_output</c> global.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> or <paramref name="global"/> is <see langword="null"/>.</exception>
    public void AddOutput(IOutput output, OutputGlobal global)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(global);
        _outputs.Add((output, global));
    }

    /// <summary>Removes an output from the overlap test. It does not send <c>leave</c> for that output.</summary>
    /// <param name="output">The output.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public void RemoveOutput(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        for (var i = 0; i < _outputs.Count; i++)
        {
            if (ReferenceEquals(_outputs[i].Output, output))
            {
                _outputs.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>
    /// Tests each surface box against each output and sends <c>enter</c> or <c>leave</c> on a change.
    /// </summary>
    /// <param name="surfaces">The surfaces and their boxes in layout coordinates.</param>
    public void Update(ReadOnlySpan<SurfaceBox> surfaces)
    {
        foreach (ref readonly var entry in surfaces)
        {
            var preferred = 1.0;
            var onAnyOutput = false;
            for (var i = 0; i < _outputs.Count; i++)
            {
                var (output, global) = _outputs[i];
                var outputBox = _layout.BoxOf(output);
                var box = entry.Box;
                var overlaps = box.X < outputBox.Right && box.Right > outputBox.X &&
                               box.Y < outputBox.Bottom && box.Bottom > outputBox.Y;
                entry.Surface.SetOutputPresence(global, overlaps);
                if (overlaps)
                {
                    onAnyOutput = true;
                    preferred = Math.Max(preferred, output.Scale);
                }
            }

            if (onAnyOutput)
            {
                _announceScale(entry.Surface, preferred);
            }
        }
    }
}
