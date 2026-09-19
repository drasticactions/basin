using Basin.Backend.Hosted;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Hosted;

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

    public HostedOutput Output { get; }

    public Scene.SceneOutput SceneOutput { get; }

    public HostedFrameTarget Target { get; private set; }

    public Action? RequestRender { get; set; }

    public event Action<BasinViewOutput>? Reconfigured;

    public bool IsPresenting { get; private set; } = true;

    internal void SetPresenting(bool presenting) => IsPresenting = presenting;

    public void Resize(int width, int height, double scale) => Reconfigure(width, height, scale, Output.Transform, requestRender: false);

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
            throw new InvalidOperationException($"the output refused {width}x{height} at scale {scale} with {transform}");
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
