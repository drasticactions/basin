using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_single_pixel_buffer_manager_v1</c> global through a <see cref="SinglePixelBufferManager"/>.
/// </summary>
public sealed class SinglePixelBufferModule : DesktopModule<SinglePixelBufferManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_single_pixel_buffer_manager_v1";

    /// <inheritdoc/>
    public override int Version => SinglePixelBufferManager.Version;

    /// <inheritdoc/>
    protected override SinglePixelBufferManager Create(BasinServices services) =>
        new(services.Display, services.Require<ClientBufferRegistry>());
}
