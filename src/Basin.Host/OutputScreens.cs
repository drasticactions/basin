using Basin.Capabilities;

namespace Basin.Host;

/// <summary>
/// Reports the outputs of an <see cref="OutputDriver"/> as screens to a UI toolkit, in the order of <see cref="OutputDriver.Views"/>.
/// </summary>
public sealed class OutputScreens : IUIScreenSource, IDisposable
{
    private readonly OutputDriver _outputs;
    private readonly OutputLayout _layout;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputScreens"/> class and subscribes to the output events of the driver.
    /// </summary>
    /// <param name="outputs">The driver whose views become screens.</param>
    /// <param name="layout">The layout that gives each screen its position.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public OutputScreens(OutputDriver outputs, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(layout);

        _outputs = outputs;
        _layout = layout;
        _outputs.Added += OnViewChanged;
        _outputs.Removed += OnViewChanged;
        _outputs.ModeChanged += OnViewChanged;
        _outputs.LayoutChanged += OnLayoutChanged;
    }

    /// <summary>
    /// Occurs when an output is added or removed, changes its mode, or the layout changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gets the number of screens. It is 0 after <see cref="Dispose"/>.
    /// </summary>
    public int Count => _disposed ? 0 : _outputs.Views.Count;

    /// <summary>
    /// Gets the name, layout box and scale of one screen. The screen at index 0 is the primary screen.
    /// </summary>
    /// <param name="index">The screen index.</param>
    /// <param name="info">The screen, or the default value when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when the index is out of range or the source is disposed.</returns>
    public bool TryGet(int index, out UIScreenInfo info)
    {
        if (_disposed || index < 0 || index >= _outputs.Views.Count)
        {
            info = default;
            return false;
        }

        var view = _outputs.Views[index];
        var box = _layout.Contains(view.Output) ? _layout.BoxOf(view.Output) : view.Box;
        info = new UIScreenInfo(
            view.Output.Name, box.X, box.Y, box.Width, box.Height, view.Output.Scale, index == 0);
        return true;
    }

    /// <summary>
    /// Unsubscribes from the driver. After this call the source reports no screens.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _outputs.Added -= OnViewChanged;
        _outputs.Removed -= OnViewChanged;
        _outputs.ModeChanged -= OnViewChanged;
        _outputs.LayoutChanged -= OnLayoutChanged;
    }

    private void OnViewChanged(OutputView view) => OnLayoutChanged();

    private void OnLayoutChanged() => Changed?.Invoke();
}
