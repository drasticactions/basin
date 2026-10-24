namespace Basin.Backend.Hosted;

/// <summary>
/// Creates outputs whose frames and render targets come from a host toolkit, not from hardware.
/// </summary>
public sealed class HostedBackend : IDisposable
{
    private readonly List<HostedOutput> _outputs = [];
    private int _outputCounter;

    /// <summary>
    /// Gets the outputs that this backend created and that are not destroyed yet.
    /// </summary>
    public IReadOnlyList<HostedOutput> Outputs => _outputs;

    /// <summary>
    /// Occurs after <see cref="CreateOutput"/> adds an output to <see cref="Outputs"/>.
    /// The output is already enabled with its mode and scale.
    /// </summary>
    public event Action<HostedOutput>? OutputAdded;

    /// <summary>
    /// Creates an enabled output and adds it to <see cref="Outputs"/>.
    /// The output leaves <see cref="Outputs"/> when it is destroyed.
    /// </summary>
    /// <param name="mode">The initial mode in physical pixels.</param>
    /// <param name="scale">The initial scale. A value that is not positive keeps the default scale.</param>
    /// <param name="name">The output name. If <see langword="null"/>, the name is <c>HOSTED-N</c> with a counter.</param>
    /// <returns>The new output.</returns>
    public HostedOutput CreateOutput(OutputMode mode, double scale = 1, string? name = null)
    {
        var output = new HostedOutput(name ?? $"HOSTED-{++_outputCounter}", mode, scale);
        _outputs.Add(output);
        output.Destroyed += () => _outputs.Remove(output);
        OutputAdded?.Invoke(output);
        return output;
    }

    /// <summary>
    /// Destroys every output in <see cref="Outputs"/>.
    /// </summary>
    public void Dispose()
    {
        foreach (var output in _outputs.ToArray())
        {
            output.Destroy();
        }
    }
}
