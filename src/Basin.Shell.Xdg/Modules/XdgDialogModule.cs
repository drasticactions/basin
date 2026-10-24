using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// Installs <c>xdg_wm_dialog_v1</c>.
/// </summary>
public sealed class XdgDialogModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "xdg_wm_dialog_v1";

    /// <inheritdoc/>
    public int Version => XdgDialogManager.Version;

    /// <summary>
    /// Gets the global that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public XdgDialogManager? Manager { get; private set; }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new XdgDialogManager(services.Display);
        services.Use(Manager);
        return Manager;
    }
}
