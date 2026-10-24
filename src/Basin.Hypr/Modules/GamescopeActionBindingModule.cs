using Basin.Capabilities;

namespace Basin.Hypr;

/// <summary>
/// Installs <c>gamescope_action_binding_manager</c>. It reads the optional <see cref="IGlobalShortcuts"/> and <see cref="IActionBindingPolicy"/> from the services.
/// </summary>
public sealed class GamescopeActionBindingModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "gamescope_action_binding_manager";

    /// <inheritdoc/>
    public int Version => ActionBindingManager.Version;

    /// <summary>
    /// Gets the manager that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public ActionBindingManager? Manager { get; private set; }

    /// <summary>
    /// Creates the <see cref="ActionBindingManager"/>. It does not register the manager in <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The registry to read the display, the shortcut registry and the policy from.</param>
    /// <returns>The manager, which destroys the global when it is disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new ActionBindingManager(
            services.Display, services.Find<IGlobalShortcuts>(), services.Find<IActionBindingPolicy>());
        return Manager;
    }
}
