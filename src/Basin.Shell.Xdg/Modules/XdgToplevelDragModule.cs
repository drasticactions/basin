using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// Installs <c>xdg_toplevel_drag_manager_v1</c>. Without an <see cref="IDragTracker"/>, the global installs but no toplevel is ever attached to a drag.
/// </summary>
public sealed class XdgToplevelDragModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "xdg_toplevel_drag_manager_v1";

    /// <inheritdoc/>
    public int Version => XdgToplevelDragManager.Version;

    /// <inheritdoc/>
    public IReadOnlyList<Type> Capabilities => [typeof(IDragTracker)];

    /// <summary>
    /// Gets the global that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public XdgToplevelDragManager? Manager { get; private set; }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new XdgToplevelDragManager(services.Display, services.Find<IDragTracker>());
        services.Use(Manager);
        return Manager;
    }
}
