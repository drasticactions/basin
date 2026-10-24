using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// Installs <c>xdg_toplevel_tag_manager_v1</c>.
/// </summary>
public sealed class XdgToplevelTagModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "xdg_toplevel_tag_manager_v1";

    /// <inheritdoc/>
    public int Version => XdgToplevelTagManager.Version;

    /// <summary>
    /// Gets the global that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public XdgToplevelTagManager? Manager { get; private set; }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new XdgToplevelTagManager(services.Display);
        services.Use(Manager);
        return Manager;
    }
}
