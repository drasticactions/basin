using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>ext_foreign_toplevel_list_v1</c> global through a <see cref="ForeignToplevelListManager"/>. It lists the windows of <see cref="IToplevelModel"/>.
/// </summary>
public sealed class ForeignToplevelListModule : DesktopModule<ForeignToplevelListManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "ext_foreign_toplevel_list_v1";

    /// <inheritdoc/>
    public override int Version => ForeignToplevelListManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IToplevelModel)];

    /// <inheritdoc/>
    protected override ForeignToplevelListManager Create(BasinServices services) =>
        new(services.Display, services.Find<IToplevelModel>());
}
