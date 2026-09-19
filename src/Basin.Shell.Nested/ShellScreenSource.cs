using Basin.Capabilities;

namespace Basin.Shell.Nested;

public sealed class ShellScreenSource : IUIScreenSource
{
    private Box _output;
    private double _scale;

    public ShellScreenSource(Box output, double scale, OutputTransform transform = OutputTransform.Normal)
    {
        _output = output;
        _scale = scale;
        Transform = transform;
    }

    public OutputTransform Transform { get; private set; }

    public int Count => 1;

    public event Action? Changed;

    public bool TryGet(int index, out UIScreenInfo info)
    {
        info = new UIScreenInfo(NestedShell.OutputKey, _output.X, _output.Y, _output.Width, _output.Height, _scale, true);
        return index == 0;
    }

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
