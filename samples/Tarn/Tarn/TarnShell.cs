using Basin;
using Basin.Hosted;
using Basin.Shell.Nested;
using Basin.Transport.Waypipe;

namespace Tarn;

public sealed class TarnShell : IDisposable
{
    private readonly Action<Basin.Scene.FrameTick> _tick;

    public TarnShell(
        BasinCompositorHost host,
        int width,
        int height,
        double scale,
        Action<Action> postToUi,
        Action<Action> postToCompositor)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(postToUi);
        ArgumentNullException.ThrowIfNull(postToCompositor);
        Host = host;
        PostToUi = postToUi;
        PostToCompositor = postToCompositor;
        View = host.CreateViewOutput(width, height, scale, NestedShell.OutputKey);
        var settings = new ShellSettings();
        Shell = new NestedShell(
            host,
            View,
            settings,
            new PanelLayout(0, 0, 0),
            KeyTable.Build(settings.Keys, []),
            postToCompositor,
            [TarnTheme.Load]);
        _tick = _ => Shell.Tick(Environment.TickCount64);
        host.Session.BeforeDispatch += _tick;
    }

    public BasinCompositorHost Host { get; }

    public BasinViewOutput View { get; }

    public NestedShell Shell { get; }

    public Action<Action> PostToUi { get; }

    public Action<Action> PostToCompositor { get; }

    public static WaypipeChannelOptions ChannelOptions() => new()
    {
        CarriesDmabuf = TarnApp.CarriesDmabuf,
        AcceptsVideo = TarnApp.VideoDecoder is not null,
        VideoDecoder = TarnApp.VideoDecoder,
    };

    public void HandleInput(BasinViewInput input) => Shell.HandleInput(input);

    public void Resize(int physicalWidth, int physicalHeight, double scale) =>
        Shell.Resize(physicalWidth, physicalHeight, scale, OutputTransform.Normal);

    public void Dispose()
    {
        Host.Session.BeforeDispatch -= _tick;
        Shell.Dispose();
    }
}
