using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>ext_session_lock_manager_v1</c> global through a <see cref="SessionLockManager"/>. It requires an <see cref="ISessionLockDriver"/> and seeds <see cref="State"/> as the <see cref="ILockState"/>.
/// </summary>
public sealed class SessionLockModule : DesktopModule<SessionLockManager>
{
    private readonly SessionLockState _state = new();

    /// <inheritdoc/>
    public override string WireInterface => "ext_session_lock_manager_v1";

    /// <inheritdoc/>
    public override int Version => SessionLockManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(ISessionLockDriver)];

    /// <summary>
    /// Gets the lock state that the module seeds as <see cref="ILockState"/>. It exists before <see cref="IProtocolModule.Install"/> and follows the manager once it is created.
    /// </summary>
    public SessionLockState State => _state;

    /// <inheritdoc/>
    public override void SeedDefaults(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseDefault<ILockState>(_state);
    }

    /// <inheritdoc/>
    protected override SessionLockManager Create(BasinServices services)
    {
        var manager = new SessionLockManager(
            services.Display, services.Require<CompositorGlobal>(), services.Find<OutputLayout>());
        _state.Attach(manager);
        services.Require<ISessionLockDriver>().Attach(manager);
        return manager;
    }
}
