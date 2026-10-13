using Basin.Capabilities;
using Wayland.Server;

namespace Basin.XWayland;

public sealed class XWaylandOutputs : IDisposable
{
    private readonly XWaylandServer _server;
    private readonly OutputLayout _layout;
    private readonly ClientScales _clientScales;
    private readonly Func<ILogicalOutputOverrides?> _logical;
    private bool _disposed;

    public XWaylandOutputs(
        XWaylandServer server, OutputLayout layout, ClientScales clientScales, Func<ILogicalOutputOverrides?> logical)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(clientScales);
        ArgumentNullException.ThrowIfNull(logical);
        _server = server;
        _layout = layout;
        _clientScales = clientScales;
        _logical = logical;
        Scale = Compute();
        server.ClientCreated += OnClientCreated;
        server.SettingsChanged += Refresh;
        layout.Changed += Refresh;
    }

    public double Scale { get; private set; }

    public event Action<double>? ScaleChanged;

    public static double LargestScale(OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var scale = 1.0;
        foreach (var (output, _) in layout.Outputs)
        {
            scale = Math.Max(scale, output.Scale);
        }

        return scale;
    }

    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var scale = Compute();
        var changed = scale != Scale;
        Scale = scale;
        if (_server.Client is { IsDestroyed: false } client)
        {
            Apply(client);
        }

        if (changed)
        {
            ScaleChanged?.Invoke(scale);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _server.ClientCreated -= OnClientCreated;
        _server.SettingsChanged -= Refresh;
        _layout.Changed -= Refresh;
    }

    private double Compute() => _server.X11Scale.IsAuto ? LargestScale(_layout) : _server.X11Scale.Value;

    private void OnClientCreated(WlClient client)
    {
        Scale = Compute();
        Apply(client);
    }

    private void Apply(WlClient client)
    {
        _clientScales.Set(client, Scale);
        var logical = _logical();
        foreach (var (output, _) in _layout.Outputs)
        {
            if (OutputGlobal.For(output) is not { } global)
            {
                continue;
            }

            global.SetClientOverride(client, output.CurrentMode, 1);
            var box = _layout.BoxOf(output);
            var x1 = (int)Math.Round(box.X * Scale);
            var y1 = (int)Math.Round(box.Y * Scale);
            var x2 = (int)Math.Round(box.Right * Scale);
            var y2 = (int)Math.Round(box.Bottom * Scale);
            logical?.SetClientOverride(client, global, new Box(x1, y1, x2 - x1, y2 - y1));
        }
    }
}
