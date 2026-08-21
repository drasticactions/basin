using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Basin.Avalonia;

public sealed class BasinOutputView : Control
{
    private readonly Func<BasinCompositorHost> _createHost;
    private readonly bool _createOwnView;
    private CompositionCustomVisual? _visual;
    private BasinOutputVisual? _handler;

    public BasinOutputView(Func<BasinCompositorHost> createHost, bool createOwnView = true)
    {
        ArgumentNullException.ThrowIfNull(createHost);
        _createHost = createHost;
        _createOwnView = createOwnView;
    }

    public event Action<BasinCompositorHost>? HostReady;

    public event Action<Exception>? HostFailed;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_visual is not null)
        {
            return;
        }

        var element = ElementComposition.GetElementVisual(this);
        if (element is null)
        {
            return;
        }

        _handler = new BasinOutputVisual(_createHost, OnHostReady, _createOwnView, OnHostFailed);
        _visual = element.Compositor.CreateCustomVisual(_handler);
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new(Bounds.Width, Bounds.Height);
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (_visual is not null)
        {
            _visual.Size = new(Bounds.Width, Bounds.Height);
        }
    }

    public void RequestFrame()
    {
        if (_visual is { } visual)
        {
            Dispatcher.UIThread.Post(() => visual.SendHandlerMessage(BasinOutputVisual.WakeMessage));
        }
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_visual is { } visual)
        {
            Dispatcher.UIThread.Post(() => visual.SendHandlerMessage(action));
        }
    }

    public Task ShutdownAsync()
    {
        if (_visual is not { } visual)
        {
            return Task.CompletedTask;
        }

        var message = new BasinShutdownMessage();
        Dispatcher.UIThread.Post(() => visual.SendHandlerMessage(message));
        return message.Completed;
    }

    private void OnHostReady(BasinCompositorHost host)
    {
        host.Wake.Ready += RequestFrame;
        Dispatcher.UIThread.Post(() =>
        {
            HostReady?.Invoke(host);
            RequestFrame();
        });
    }

    private void OnHostFailed(Exception error) =>
        Dispatcher.UIThread.Post(() => HostFailed?.Invoke(error));
}
