using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Basin.Hosted;

namespace Basin.Avalonia;

/// <summary>
/// An Avalonia control that creates and owns a <see cref="BasinCompositorHost"/> and composites its view output.
/// The host lives on the Avalonia composition thread.
/// </summary>
public sealed class BasinOutputView : Control
{
    private readonly Func<BasinCompositorHost> _createHost;
    private readonly bool _createOwnView;
    private CompositionCustomVisual? _visual;
    private BasinOutputVisual? _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinOutputView"/> class. The host is created on the composition thread on the first render.
    /// </summary>
    /// <param name="createHost">Makes the compositor host. The control disposes the host at shutdown.</param>
    /// <param name="createOwnView"><see langword="true"/> to create a view output that fills the control, <see langword="false"/> when the caller shows the outputs elsewhere.</param>
    public BasinOutputView(Func<BasinCompositorHost> createHost, bool createOwnView = true)
    {
        ArgumentNullException.ThrowIfNull(createHost);
        _createHost = createHost;
        _createOwnView = createOwnView;
    }

    /// <summary>
    /// Occurs on the UI thread after the host is created.
    /// </summary>
    public event Action<BasinCompositorHost>? HostReady;

    /// <summary>
    /// Occurs on the UI thread when the host factory throws. The control then stays empty.
    /// </summary>
    public event Action<Exception>? HostFailed;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (_visual is not null)
        {
            _visual.Size = new(Bounds.Width, Bounds.Height);
        }
    }

    /// <summary>
    /// Starts the host on a composition visual that is not in the visual tree. A second call does nothing.
    /// </summary>
    /// <param name="compositor">The Avalonia compositor that runs the host.</param>
    /// <exception cref="InvalidOperationException">The control was made with <c>createOwnView</c> set to <see langword="true"/>.</exception>
    public void StartDetached(Compositor compositor)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        if (_visual is not null)
        {
            return;
        }

        if (_createOwnView)
        {
            throw new InvalidOperationException("A detached output view has no size for its own view output. Pass createOwnView: false.");
        }

        _handler = new BasinOutputVisual(_createHost, OnHostReady, _createOwnView, OnHostFailed);
        _visual = compositor.CreateCustomVisual(_handler);
        _visual.SendHandlerMessage(BasinOutputVisual.StartMessage);
    }

    /// <summary>
    /// Asks the composition thread to dispatch the compositor and invalidate the views that are dirty. The host calls it when its wake fd is ready.
    /// </summary>
    public void RequestFrame()
    {
        if (_visual is { } visual)
        {
            Dispatcher.UIThread.Post(() => visual.SendHandlerMessage(BasinOutputVisual.WakeMessage));
        }
    }

    /// <summary>
    /// Runs an action on the composition thread, then dispatches and flushes the compositor. It does nothing before the control has a visual.
    /// </summary>
    /// <param name="action">The work to run with the compositor's thread affinity.</param>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_visual is { } visual)
        {
            Dispatcher.UIThread.Post(() => visual.SendHandlerMessage(action));
        }
    }

    /// <summary>
    /// Asks the composition thread to dispose the view output and the host.
    /// </summary>
    /// <returns>A task that completes when the host is disposed. It is already complete when the control never got a visual.</returns>
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
