using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>zwp_virtual_keyboard_manager_v1</c> global through a <see cref="VirtualKeyboardManager"/>. It injects keys through <see cref="IInputSink"/>.
/// </summary>
public sealed class VirtualKeyboardModule : DesktopModule<VirtualKeyboardManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "zwp_virtual_keyboard_manager_v1";

    /// <inheritdoc/>
    public override int Version => VirtualKeyboardManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IInputSink)];

    /// <inheritdoc/>
    protected override VirtualKeyboardManager Create(BasinServices services) =>
        new(services.Display, services.Find<IInputSink>());
}
