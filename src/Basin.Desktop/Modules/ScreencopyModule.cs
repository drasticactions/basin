using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwlr_screencopy_manager_v1</c> global through a <see cref="ScreencopyManager"/>. It copies frames through <see cref="IScreenCapture"/> when one is registered.
/// </summary>
public sealed class ScreencopyModule : DesktopModule<ScreencopyManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwlr_screencopy_manager_v1";

    /// <inheritdoc/>
    public override int Version => ScreencopyManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IScreenCapture)];

    /// <inheritdoc/>
    protected override ScreencopyManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<OutputLayout>(),
            services.Require<ClientBufferRegistry>(),
            services.Find<IScreenCapture>(),
            services.Find<ICaptureDmabufConstraints>());
}
