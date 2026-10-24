using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwlr_output_manager_v1</c> global through a <see cref="OutputManagementManager"/>. It seeds layout-backed <see cref="IOutputConfiguration"/> and <see cref="IOutputSet"/> defaults when an <see cref="OutputLayout"/> is registered.
/// </summary>
public sealed class OutputManagementModule : DesktopModule<OutputManagementManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwlr_output_manager_v1";

    /// <inheritdoc/>
    public override int Version => OutputManagementManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IOutputConfiguration), typeof(IOutputSet)];

    /// <inheritdoc/>
    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Find<OutputLayout>() is { } layout)
        {
            services.UseDefault<IOutputConfiguration>(new LayoutOutputConfiguration(layout));
            services.UseDefault<IOutputSet>(new LayoutOutputSet(layout));
        }
    }

    /// <inheritdoc/>
    protected override OutputManagementManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<OutputLayout>(),
            services.Require<IOutputSet>(),
            services.Find<IOutputConfiguration>());
}
