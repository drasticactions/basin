using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

/// <summary>
/// The protocol modules that every compositor needs.
/// </summary>
public static class CorePack
{
    /// <summary>
    /// Gets a new pack with <c>wl_shm</c>, <c>wl_compositor</c>, <c>wl_subcompositor</c>, <c>wl_fixes</c>, <c>wp_viewporter</c> and <c>wp_presentation</c>.
    /// </summary>
    public static ProtocolPack Default => new(
    [
        new ShmModule(),
        new CompositorModule(),
        new SubcompositorModule(),
        new FixesModule(),
        new ViewporterModule(),
        new PresentationTimeModule(),
    ]);
}
