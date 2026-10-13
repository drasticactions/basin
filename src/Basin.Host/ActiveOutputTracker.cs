namespace Basin.Host;

public sealed class ActiveOutputTracker
{
    private readonly OutputLayout _layout;
    private readonly List<IOutput> _outputs = [];

    public ActiveOutputTracker(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    public IOutput? Current { get; private set; }

    public event Action<IOutput>? Changed;

    public void Moved(double x, double y)
    {
        if (_layout.OutputAt(x, y) is { } output && !ReferenceEquals(output, Current))
        {
            Current = output;
            Changed?.Invoke(output);
        }
    }

    public void Added(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!_outputs.Contains(output))
        {
            _outputs.Add(output);
        }

        Current ??= output;
    }

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
