using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwlr_virtual_pointer_manager_v1</c> global through a <see cref="VirtualPointerManager"/>. It injects pointer events through <see cref="IInputSink"/>.
/// </summary>
public sealed class VirtualPointerModule : DesktopModule<VirtualPointerManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwlr_virtual_pointer_manager_v1";

    /// <inheritdoc/>
    public override int Version => VirtualPointerManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IInputSink)];

    /// <inheritdoc/>
    protected override VirtualPointerManager Create(BasinServices services) =>
        new(services.Display, services.Find<IInputSink>());
}
