using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_pointer_constraints_v1</c> global through a <see cref="PointerConstraintsManager"/>.
/// </summary>
public sealed class PointerConstraintsModule : DesktopModule<PointerConstraintsManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwp_pointer_constraints_v1";

    /// <inheritdoc/>
    public override int Version => PointerConstraintsManager.Version;

    /// <inheritdoc/>
    protected override PointerConstraintsManager Create(BasinServices services)
    {
        var manager = new PointerConstraintsManager(services.Display, services.Require<CompositorGlobal>(), services.Find<Seat.Seat>());
        if (manager.Driver is { } driver)
        {
            services.Use(driver);
        }

        return manager;
    }
}
