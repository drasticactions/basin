using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;

namespace Basin.Hypr;

/// <summary>
/// Installs <c>hyprland_lock_notifier_v1</c> over the <see cref="ILockState"/> of the compositor.
/// </summary>
public sealed class HyprlandLockNotifyModule : DesktopModule<HyprlandLockNotifier>
{
    /// <inheritdoc/>
    public override string WireInterface => "hyprland_lock_notifier_v1";

    /// <inheritdoc/>
    public override int Version => HyprlandLockNotifier.Version;

    /// <summary>
    /// Gets the capabilities that the module consumes. The module reads <see cref="ILockState"/>.
    /// </summary>
    public override IReadOnlyList<Type> Capabilities => [typeof(ILockState)];

    /// <summary>
    /// Registers <see cref="NeverLocked.Instance"/> as the default lock state, so that a lock state from the consumer wins.
    /// </summary>
    /// <param name="services">The registry to seed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<ILockState>(NeverLocked.Instance);
    }

    /// <summary>
    /// Creates the notifier over the display and the <see cref="ILockState"/>.
    /// </summary>
    /// <param name="services">The registry to read the services from.</param>
    /// <returns>The new notifier.</returns>
    protected override HyprlandLockNotifier Create(BasinServices services) =>
        new(services.Display, services.Find<ILockState>());
}
