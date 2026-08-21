using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Basin.Avalonia;

public sealed class BasinToplevelView : Control
{
    private readonly BasinCompositorHost _host;
    private readonly Func<BasinCompositorHost, BasinViewOutput> _createView;
    private CompositionCustomVisual? _visual;

    public BasinToplevelView(BasinCompositorHost host, Func<BasinCompositorHost, BasinViewOutput> createView)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(createView);
        _host = host;
        _createView = createView;
        Focusable = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var element = ElementComposition.GetElementVisual(this);
        if (element is null)
        {
            return;
        }

        if (_visual is null)
        {
            _visual = element.Compositor.CreateCustomVisual(new BasinViewVisual(_host, _createView));
        }

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
}
