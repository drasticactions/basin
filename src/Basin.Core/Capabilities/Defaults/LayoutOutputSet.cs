namespace Basin.Capabilities.Defaults;

/// <summary>
/// Lists every output that was ever in a layout and is not destroyed. An output that leaves the layout stays in the set.
/// </summary>
public sealed class LayoutOutputSet : IOutputSet
{
    private readonly OutputLayout _layout;
    private readonly List<IOutput> _outputs = [];
    private readonly Dictionary<IOutput, Action> _destroyed = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="LayoutOutputSet"/> class and adds the outputs that the layout holds now.
    /// </summary>
    /// <param name="layout">The layout to watch.</param>
    public LayoutOutputSet(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        layout.Changed += Absorb;
        Absorb();
    }

    /// <inheritdoc/>
    public IReadOnlyList<IOutput> Outputs => _outputs;

    /// <inheritdoc/>
    public event Action? Changed;

    private void Absorb()
    {
        var added = false;
        foreach (var (output, _) in _layout.Outputs)
        {
            if (_destroyed.ContainsKey(output))
            {
                continue;
            }

            _outputs.Add(output);
            void OnDestroyed() => Forget(output);
            _destroyed[output] = OnDestroyed;
            output.Destroyed += OnDestroyed;
            added = true;
        }

        if (added)
        {
            Changed?.Invoke();
        }
    }

    private void Forget(IOutput output)
    {
        if (!_destroyed.Remove(output, out var handler))
        {
            return;
        }

        output.Destroyed -= handler;
        _outputs.Remove(output);
        Changed?.Invoke();
    }
}
