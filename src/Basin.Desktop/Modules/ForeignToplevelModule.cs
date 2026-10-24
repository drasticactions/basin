using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwlr_foreign_toplevel_manager_v1</c> global through a <see cref="ForeignToplevelManager"/>. It lists and controls the windows of <see cref="IToplevelModel"/>.
/// </summary>
public sealed class ForeignToplevelModule : DesktopModule<ForeignToplevelManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwlr_foreign_toplevel_manager_v1";

    /// <inheritdoc/>
    public override int Version => ForeignToplevelManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel)];

    /// <inheritdoc/>
    protected override ForeignToplevelManager Create(BasinServices services) =>
        new(services.Display, services.Find<IToplevelModel>());
}
