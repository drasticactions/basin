using System.Diagnostics.CodeAnalysis;
using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Color;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

/// <summary>
/// Registers output color configuration and the LUT cache, and later attaches the drivers that send color descriptions to outputs and surfaces.
/// </summary>
public sealed class ColorCapabilityPack : ICapabilityPack
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ColorCapabilityPack"/> class.
    /// </summary>
    /// <param name="layout">The layout whose outputs the configuration describes.</param>
    /// <param name="renderer">The renderer that builds the color LUTs.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> or <paramref name="renderer"/> is <see langword="null"/>.</exception>
    public ColorCapabilityPack(OutputLayout layout, IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(renderer);
        Layout = new LayoutOutputConfiguration(layout);
        Configuration = new ColorOutputConfiguration(Layout);
        Luts = new ColorLutCache(renderer);
    }

    /// <summary>
    /// Gets the color configuration that the pack registers as <see cref="IOutputConfiguration"/> and as the default <see cref="IOutputLooks"/>.
    /// </summary>
    public ColorOutputConfiguration Configuration { get; }

    /// <summary>
    /// Gets the layout configuration that <see cref="Configuration"/> wraps.
    /// </summary>
    public LayoutOutputConfiguration Layout { get; }

    /// <summary>
    /// Gets the LUT cache that the pack registers as the default <see cref="IColorTransformResolver"/>.
    /// </summary>
    public ColorLutCache Luts { get; }

    /// <summary>
    /// Gets the color manager that <see cref="Attach"/> found. It is <see langword="null"/> before a successful attach.
    /// </summary>
    public ColorManager? Manager { get; private set; }

    /// <summary>
    /// Gets the driver that sends output descriptions. It is <see langword="null"/> before a successful <see cref="Attach"/>.
    /// </summary>
    public OutputColorDriver? Outputs { get; private set; }

    /// <summary>
    /// Gets the driver that attaches surface LUTs. It is <see langword="null"/> before a successful <see cref="Attach"/>.
    /// </summary>
    public SurfaceLutDriver? Surfaces { get; private set; }

    /// <inheritdoc/>
    public void Register(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Use<IOutputConfiguration>(Configuration);
        services.UseDefault<IColorTransformResolver>(Luts);
        services.UseDefault<IOutputLooks>(Configuration);
    }

    /// <summary>
    /// Creates the output and surface drivers after <see cref="BasinServices.Freeze"/>. It also watches xdg toplevels when an <see cref="XdgShell"/> is registered.
    /// </summary>
    /// <param name="services">The frozen registry to find the <see cref="ColorManager"/> in.</param>
    /// <param name="scene">The scene whose surfaces get color transforms.</param>
    /// <returns><see langword="true"/> when the drivers exist, or <see langword="false"/> when no color manager is installed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="scene"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The drivers are already attached.</exception>
    [MemberNotNullWhen(true, nameof(Manager), nameof(Outputs), nameof(Surfaces))]
    public bool Attach(BasinServices services, Scene.Scene scene)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scene);
        if (Manager is not null)
        {
            throw new InvalidOperationException("the color drivers are already attached");
        }

        if (services.Find<ColorManager>() is not { } color)
        {
            return false;
        }

        Manager = color;
        Outputs = new OutputColorDriver(color, Configuration);
        Surfaces = new SurfaceLutDriver(scene, color, Luts);
        if (services.Find<XdgShell>() is { } shell)
        {
            Surfaces.WatchToplevels(shell);
        }

        return true;
    }

    /// <summary>
    /// Adds an output to the output driver. It does nothing before a successful <see cref="Attach"/>.
    /// </summary>
    /// <param name="global">The output's global.</param>
    /// <param name="output">The output to describe.</param>
    /// <param name="scene">The scene output that gets the blend space, or <see langword="null"/>.</param>
    public void AddOutput(OutputGlobal global, IOutput output, SceneOutput? scene) =>
        Outputs?.Add(global, output, scene);

    /// <summary>
    /// Removes an output from the output driver. It does nothing before a successful <see cref="Attach"/>.
    /// </summary>
    /// <param name="global">The output's global.</param>
    public void RemoveOutput(OutputGlobal global) => Outputs?.Remove(global);

    /// <summary>
    /// Describes every scene surface again. It does nothing before a successful <see cref="Attach"/>.
    /// </summary>
    public void RefreshSurfaces() => Surfaces?.Refresh();
}
