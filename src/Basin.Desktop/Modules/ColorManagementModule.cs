using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_color_manager_v1</c> global through a <see cref="ColorManager"/>. It requires an <see cref="IColorTransformResolver"/> and reads <see cref="IColorProfileService"/> when one is registered.
/// </summary>
public sealed class ColorManagementModule : DesktopModule<ColorManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_color_manager_v1";

    /// <inheritdoc/>
    public override int Version => ColorManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IColorProfileService)];

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(IColorTransformResolver)];

    /// <inheritdoc/>
    protected override ColorManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>())
        {
            Profiles = services.Find<IColorProfileService>(),
            Resolver = services.Require<IColorTransformResolver>(),
        };
}
