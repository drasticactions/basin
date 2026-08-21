namespace Basin.Backend.Headless;

public sealed class HeadlessBackend : IDisposable
{
    private readonly ICompositorEventLoop _loop;
    private readonly List<HeadlessOutput> _outputs = [];
    private int _outputCounter;
    private int _touchScreenCounter;
    private int _keyboardCounter;
    private int _pointerCounter;

    public HeadlessBackend(ICompositorEventLoop loop)
    {
        _loop = loop;
    }

    public IReadOnlyList<HeadlessOutput> Outputs => _outputs;

    public event Action<HeadlessOutput>? OutputAdded;

    public HeadlessOutput CreateOutput(OutputMode mode, bool manualFrameClock = false, string? name = null)
    {
        var output = new HeadlessOutput(_loop, name ?? $"HEADLESS-{++_outputCounter}", mode, manualFrameClock);
        _outputs.Add(output);
        output.Destroyed += () => _outputs.Remove(output);
        OutputAdded?.Invoke(output);
        return output;
    }

    public HeadlessTouchScreen CreateTouchScreen(string? name = null) =>
        new(name ?? $"headless-touch-{++_touchScreenCounter}");

    public HeadlessKeyboard CreateKeyboard(string? name = null) =>
        new(name ?? $"headless-keyboard-{++_keyboardCounter}");

    public HeadlessPointer CreatePointer(string? name = null) =>
        new(name ?? $"headless-pointer-{++_pointerCounter}");

    public HeadlessTablet CreateTablet() => new();

    public void Dispose()
    {
        foreach (var output in _outputs.ToArray())
        {
            output.Destroy();
        }
    }
}
