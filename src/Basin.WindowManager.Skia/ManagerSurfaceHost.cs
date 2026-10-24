using Basin.WindowManager.Skia.Protocol;
using Wayland;

namespace Basin.WindowManager.Skia;

/// <summary>
/// What every layer surface of a manager needs: the compositor, shm, the output scales and <c>zwlr_layer_shell_v1</c>, which binds when the global arrives.
/// </summary>
public sealed class ManagerSurfaceHost
{
    private ManagerSurfaceHost(RiverWindowManager wm, WlCompositor compositor, WlShm shm)
    {
        Manager = wm;
        Compositor = compositor;
        Shm = shm;
        Scales = new OutputScales(wm);
        var registry = wm.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "zwlr_layer_shell_v1")
            {
                LayerShell = registry.Bind<ZwlrLayerShellV1>(e.Name, Math.Min(e.Version, 4));
            }
        };
    }

    /// <summary>
    /// Gets the manager whose connection this host binds on.
    /// </summary>
    public RiverWindowManager Manager { get; }

    /// <summary>
    /// Gets the manager's compositor global.
    /// </summary>
    public WlCompositor Compositor { get; }

    /// <summary>
    /// Gets the manager's shm global.
    /// </summary>
    public WlShm Shm { get; }

    /// <summary>
    /// Gets the output scales that this host binds.
    /// </summary>
    public OutputScales Scales { get; }

    /// <summary>
    /// Gets the bound layer shell, or <see langword="null"/> until the global arrives on a dispatch, or when the compositor does not serve it.
    /// </summary>
    public ZwlrLayerShellV1? LayerShell { get; private set; }

    /// <summary>
    /// Creates a host for a manager.
    /// </summary>
    /// <param name="wm">The manager to bind on.</param>
    /// <returns>The host, or <see langword="null"/> when the manager has no compositor or no shm global.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
    public static ManagerSurfaceHost? TryCreate(RiverWindowManager wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        return wm is { Compositor: { } compositor, Shm: { } shm } ? new ManagerSurfaceHost(wm, compositor, shm) : null;
    }

    /// <summary>
    /// Gets the bound <c>wl_output</c> of a river output.
    /// </summary>
    /// <param name="output">The river output.</param>
    /// <returns>The proxy, or <see langword="null"/> when its global is not bound or is gone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public WlOutput? ProxyFor(WmOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return Scales.ProxyForName(output.WlOutputName);
    }

    /// <summary>
    /// Creates a layer surface on an output.
    /// </summary>
    /// <param name="output">The output to show the surface on.</param>
    /// <param name="layer">The layer to place the surface in.</param>
    /// <param name="scope">The namespace that the compositor sees.</param>
    /// <returns>The new surface, which the caller owns and disposes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The layer shell is not bound.</exception>
    public ManagerSurface Create(WlOutput output, ZwlrLayerShellV1.Layer layer, string scope)
    {
        ArgumentNullException.ThrowIfNull(output);
        var shell = LayerShell ?? throw new InvalidOperationException("the compositor has not offered zwlr_layer_shell_v1");
        return new ManagerSurface(Compositor, Shm, shell, output, layer, scope);
    }
}
