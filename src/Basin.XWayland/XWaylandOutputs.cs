using Basin.Capabilities;
using Wayland.Server;

namespace Basin.XWayland;

/// <summary>
/// Gives the Xwayland client its scale and the per-client output overrides, so that X11 sees outputs in physical pixels. It updates both when the layout or the server settings change.
/// </summary>
public sealed class XWaylandOutputs : IDisposable
{
    private readonly XWaylandServer _server;
    private readonly OutputLayout _layout;
    private readonly ClientScales _clientScales;
    private readonly Func<ILogicalOutputOverrides?> _logical;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="XWaylandOutputs"/> class and subscribes to the server and the layout.
    /// </summary>
    /// <param name="server">The server whose client gets the overrides.</param>
    /// <param name="layout">The layout that supplies the outputs and their scales.</param>
    /// <param name="clientScales">The store for the client scale.</param>
    /// <param name="logical">A function that returns the logical output overrides, or <see langword="null"/> when none exist. It is called on each update.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets the current X11 scale: the fixed server scale, or the largest output scale when the scale is automatic.
    /// </summary>
    public double Scale { get; private set; }

    /// <summary>
    /// Occurs when <see cref="Refresh"/> computes a new scale. The argument is the new scale.
    /// </summary>
    public event Action<double>? ScaleChanged;

    /// <summary>
    /// Finds the largest output scale in a layout.
    /// </summary>
    /// <param name="layout">The layout to search.</param>
    /// <returns>The largest scale, and never less than 1.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Computes the scale again and applies the overrides to the running client. It does nothing after dispose.
    /// </summary>
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

    /// <summary>
    /// Unsubscribes from the server and the layout. Overrides that are already set stay in effect.
    /// </summary>
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
