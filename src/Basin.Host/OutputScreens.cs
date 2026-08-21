using Basin.Capabilities;

namespace Basin.Host;

public sealed class OutputScreens : IUIScreenSource, IDisposable
{
    private readonly OutputDriver _outputs;
    private readonly OutputLayout _layout;
    private bool _disposed;

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

    public event Action? Changed;

    public int Count => _disposed ? 0 : _outputs.Views.Count;

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
