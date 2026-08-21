using Basin.Backend.Hosted;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Render.Avalonia;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Avalonia;

public sealed class BasinViewOutput : IDisposable
{
    private readonly BasinCompositorHost _host;
    private bool _disposed;

    internal BasinViewOutput(BasinCompositorHost host, HostedOutput output, Scene.SceneOutput sceneOutput)
    {
        _host = host;
        Output = output;
        SceneOutput = sceneOutput;
        Target = new AvaloniaFrameTarget(output.CurrentMode.Width, output.CurrentMode.Height, output.Scale);
    }

    public HostedOutput Output { get; }

    public Scene.SceneOutput SceneOutput { get; }

    public AvaloniaFrameTarget Target { get; private set; }

    public Action? RequestRender { get; set; }

    public void Resize(int width, int height, double scale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Target.Width == width && Target.Height == height && Math.Abs(Target.Scale - scale) < double.Epsilon)
        {
            return;
        }

        Output.Resize(width, height, scale);
        var stale = Target;
        Target = new AvaloniaFrameTarget(width, height, scale);
        stale.Destroy();
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
