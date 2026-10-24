namespace Basin.Backend.Headless;

/// <summary>
/// Creates outputs and input devices that have no hardware behind them.
/// Tests and off-screen compositors use it to drive frames and input by hand.
/// </summary>
public sealed class HeadlessBackend : IDisposable
{
    private readonly ICompositorEventLoop _loop;
    private readonly List<HeadlessOutput> _outputs = [];
    private int _outputCounter;
    private int _touchScreenCounter;
    private int _keyboardCounter;
    private int _pointerCounter;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeadlessBackend"/> class.
    /// </summary>
    /// <param name="loop">The compositor loop that owns the frame timers of the outputs.</param>
    public HeadlessBackend(ICompositorEventLoop loop)
    {
        _loop = loop;
    }

    /// <summary>
    /// Gets the outputs that this backend created and that are not destroyed yet.
    /// </summary>
    public IReadOnlyList<HeadlessOutput> Outputs => _outputs;

    /// <summary>
    /// Occurs after <see cref="CreateOutput"/> adds an output to <see cref="Outputs"/>.
    /// The output is already enabled with its mode.
    /// </summary>
    public event Action<HeadlessOutput>? OutputAdded;

    /// <summary>
    /// Creates an enabled output with the given mode and adds it to <see cref="Outputs"/>.
    /// The output leaves <see cref="Outputs"/> when it is destroyed.
    /// </summary>
    /// <param name="mode">The initial mode. The refresh rate sets the frame timer interval.</param>
    /// <param name="manualFrameClock">If <see langword="true"/>, the output presents only when <see cref="HeadlessOutput.StepFrame"/> is called.</param>
    /// <param name="name">The output name. If <see langword="null"/>, the name is <c>HEADLESS-N</c> with a counter.</param>
    /// <returns>The new output.</returns>
    public HeadlessOutput CreateOutput(OutputMode mode, bool manualFrameClock = false, string? name = null)
    {
        var output = new HeadlessOutput(_loop, name ?? $"HEADLESS-{++_outputCounter}", mode, manualFrameClock);
        _outputs.Add(output);
        output.Destroyed += () => _outputs.Remove(output);
        OutputAdded?.Invoke(output);
        return output;
    }

    /// <summary>
    /// Creates a touch screen that raises only the events that the caller injects.
    /// The backend does not keep a reference to it.
    /// </summary>
    /// <param name="name">The device name. If <see langword="null"/>, the name is <c>headless-touch-N</c>.</param>
    /// <returns>The new touch screen.</returns>
    public HeadlessTouchScreen CreateTouchScreen(string? name = null) =>
        new(name ?? $"headless-touch-{++_touchScreenCounter}");

    /// <summary>
    /// Creates a keyboard that raises only the events that the caller injects.
    /// The backend does not keep a reference to it.
    /// </summary>
    /// <param name="name">The device name. If <see langword="null"/>, the name is <c>headless-keyboard-N</c>.</param>
    /// <returns>The new keyboard.</returns>
    public HeadlessKeyboard CreateKeyboard(string? name = null) =>
        new(name ?? $"headless-keyboard-{++_keyboardCounter}");

    /// <summary>
    /// Creates a pointer that raises only the events that the caller injects.
    /// The backend does not keep a reference to it.
    /// </summary>
    /// <param name="name">The device name. If <see langword="null"/>, the name is <c>headless-pointer-N</c>.</param>
    /// <returns>The new pointer.</returns>
    public HeadlessPointer CreatePointer(string? name = null) =>
        new(name ?? $"headless-pointer-{++_pointerCounter}");

    /// <summary>
    /// Creates an empty tablet source. The caller adds tablets, tools and pads to it.
    /// </summary>
    /// <returns>The new tablet source.</returns>
    public HeadlessTablet CreateTablet() => new();

    /// <summary>
    /// Destroys every output in <see cref="Outputs"/>. Input devices are not affected.
    /// </summary>
    public void Dispose()
    {
        foreach (var output in _outputs.ToArray())
        {
            output.Destroy();
        }
    }
}
