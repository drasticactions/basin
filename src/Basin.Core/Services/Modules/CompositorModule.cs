using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

/// <summary>
/// Installs <c>wl_compositor</c>, registers the <see cref="CompositorGlobal"/>, and seeds the default surface services.
/// </summary>
public sealed class CompositorModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "wl_compositor";

    /// <inheritdoc/>
    public int Version => CompositorGlobal.Version;

    /// <summary>
    /// Gets the <c>wl_compositor</c> global that <see cref="Install"/> made, or <see langword="null"/> before the install.
    /// </summary>
    public CompositorGlobal? Global { get; private set; }

    /// <inheritdoc/>
    public void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.UseDefault(new ClientBufferRegistry());
        services.UseDefault(new ClientScales());
        var popups = new Capabilities.Defaults.PopupDirectory();
        services.UseDefault(popups);
        services.UseDefault<IPopupDirectory>(popups);
        services.UseDefault(new PresentedSets());
        services.UseDefault<Capabilities.ISurfaceHints>(new Capabilities.Defaults.SurfaceHints());
        services.UseDefault<Capabilities.IFrameStats>(new WindowFrameStats(services));
    }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Global = new CompositorGlobal(services.Display, services.Require<ClientBufferRegistry>(), services.Require<ClientScales>())
        {
            Loop = services.Loop,
            PresentedSets = services.Find<PresentedSets>() ?? new PresentedSets(),
        };
        services.Use(Global);
        services.UseDefault<Capabilities.ISurfaceDiagnostics>(new SurfaceDiagnostics(Global));
        return Global;
    }
}
