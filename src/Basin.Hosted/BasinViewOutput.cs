using Basin.Backend.Hosted;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Hosted;

/// <summary>
/// One hosted output that a host view draws, with the scene output that renders it and its frame target. Clients never see it unless the consumer advertises it.
/// </summary>
public sealed class BasinViewOutput : IDisposable
{
    private readonly BasinCompositorHost _host;
    private bool _disposed;

    internal BasinViewOutput(BasinCompositorHost host, HostedOutput output, Scene.SceneOutput sceneOutput)
    {
        _host = host;
        Output = output;
        SceneOutput = sceneOutput;
        Target = new HostedFrameTarget(output.CurrentMode.Width, output.CurrentMode.Height, output.Scale);
    }

    /// <summary>
    /// Gets the hosted output. The view output destroys it on dispose.
    /// </summary>
    public HostedOutput Output { get; }

    /// <summary>
    /// Gets the scene output that commits the frames of this view.
    /// </summary>
    public Scene.SceneOutput SceneOutput { get; }

    /// <summary>
    /// Gets the frame target for the current size and scale. A reconfigure replaces it and destroys the previous one.
    /// </summary>
    public HostedFrameTarget Target { get; private set; }

    /// <summary>
    /// Gets or sets the callback that asks the host view for a frame. The host calls it on resume, on a dirty scene and after a reconfigure.
    /// </summary>
    public Action? RequestRender { get; set; }

    /// <summary>
    /// Occurs after the mode, scale, transform or refresh rate changed.
    /// </summary>
    public event Action<BasinViewOutput>? Reconfigured;

    /// <summary>
    /// Gets a value indicating whether the view can present. It is <see langword="false"/> while the host is suspended.
    /// </summary>
    public bool IsPresenting { get; private set; } = true;

    internal void SetPresenting(bool presenting) => IsPresenting = presenting;

    /// <summary>
    /// Changes the size and scale and keeps the transform. Unlike <see cref="Reconfigure(int, int, double, OutputTransform)"/>, it does not call <see cref="RequestRender"/>.
    /// </summary>
    /// <param name="width">The new mode width in pixels.</param>
    /// <param name="height">The new mode height in pixels.</param>
    /// <param name="scale">The new output scale.</param>
    /// <exception cref="InvalidOperationException">The output refused the new state.</exception>
    /// <exception cref="ObjectDisposedException">The view output is disposed.</exception>
    public void Resize(int width, int height, double scale) => Reconfigure(width, height, scale, Output.Transform, requestRender: false);

    /// <summary>
    /// Changes the size, scale and transform in one output commit, then raises <see cref="Reconfigured"/> and calls <see cref="RequestRender"/>.
    /// An unchanged state does nothing.
    /// </summary>
    /// <param name="width">The new mode width in pixels.</param>
    /// <param name="height">The new mode height in pixels.</param>
    /// <param name="scale">The new output scale.</param>
    /// <param name="transform">The new transform.</param>
    /// <exception cref="InvalidOperationException">The output refused the new state.</exception>
    /// <exception cref="ObjectDisposedException">The view output is disposed.</exception>
    public void Reconfigure(int width, int height, double scale, OutputTransform transform) =>
        Reconfigure(width, height, scale, transform, requestRender: true);

    private void Reconfigure(int width, int height, double scale, OutputTransform transform, bool requestRender)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Target.Width == width && Target.Height == height && Math.Abs(Target.Scale - scale) < double.Epsilon &&
            Output.Transform == transform)
        {
            return;
        }

        if (!Output.Reconfigure(width, height, scale, transform))
        {
            throw new InvalidOperationException($"The output refused {width}x{height} at scale {scale} with transform {transform}.");
        }

        var stale = Target;
        Target = new HostedFrameTarget(width, height, scale);
        stale.Destroy();
        _host.Screens.NotifyReconfigured(Output);
        Reconfigured?.Invoke(this);
        if (requestRender)
        {
            RequestRender?.Invoke();
        }
    }

    /// <summary>
    /// Changes the refresh rate of the mode and raises <see cref="Reconfigured"/>. An unchanged rate does nothing.
    /// </summary>
    /// <param name="refreshMilliHz">The refresh rate in millihertz.</param>
    /// <exception cref="InvalidOperationException">The output refused the rate.</exception>
    /// <exception cref="ObjectDisposedException">The view output is disposed.</exception>
    public void SetRefresh(int refreshMilliHz)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Output.CurrentMode.RefreshMilliHz == refreshMilliHz)
        {
            return;
        }

        if (!Output.SetRefresh(refreshMilliHz))
        {
            throw new InvalidOperationException($"The output refused a refresh rate of {refreshMilliHz} mHz.");
        }

        _host.Screens.NotifyReconfigured(Output);
        Reconfigured?.Invoke(this);
    }

    /// <summary>
    /// Removes the view from the host and the session, and destroys the scene output, the hosted output and the frame target.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.ForgetView(this);
        _host.Session.RemoveOutput(SceneOutput);
        SceneOutput.Dispose();
        Output.Destroy();
        Target.Destroy();
    }
}
