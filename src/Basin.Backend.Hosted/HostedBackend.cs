namespace Basin.Backend.Hosted;

public sealed class HostedBackend : IDisposable
{
    private readonly List<HostedOutput> _outputs = [];
    private int _outputCounter;

    public IReadOnlyList<HostedOutput> Outputs => _outputs;

    public event Action<HostedOutput>? OutputAdded;

    public HostedOutput CreateOutput(OutputMode mode, double scale = 1, string? name = null)
    {
        var output = new HostedOutput(name ?? $"HOSTED-{++_outputCounter}", mode, scale);
        _outputs.Add(output);
        output.Destroyed += () => _outputs.Remove(output);
        OutputAdded?.Invoke(output);
        return output;
    }

    public void Dispose()
    {
        foreach (var output in _outputs.ToArray())
        {
            output.Destroy();
        }
    }
}
