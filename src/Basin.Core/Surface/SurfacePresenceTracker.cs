namespace Basin;

public sealed class SurfacePresenceTracker
{
    private readonly OutputLayout _layout;
    private readonly Action<Surface, double> _announceScale;
    private readonly List<(IOutput Output, OutputGlobal Global)> _outputs = [];

    public SurfacePresenceTracker(OutputLayout layout, Action<Surface, double> announceScale)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(announceScale);
        _layout = layout;
        _announceScale = announceScale;
    }

    public void AddOutput(IOutput output, OutputGlobal global)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(global);
        _outputs.Add((output, global));
    }

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
