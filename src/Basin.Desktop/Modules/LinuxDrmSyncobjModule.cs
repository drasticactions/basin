using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_linux_drm_syncobj_manager_v1</c> global through a <see cref="LinuxDrmSyncobjManager"/>. The protocol has no failure event, so the module installs nothing when no <see cref="IDrmSyncDevice"/> is registered.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxDrmSyncobjModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "wp_linux_drm_syncobj_manager_v1";

    /// <inheritdoc/>
    public int Version => LinuxDrmSyncobjManager.Version;

    /// <inheritdoc/>
    public IReadOnlyList<Type> Capabilities => [typeof(IDrmSyncDevice)];

    /// <summary>
    /// Gets the manager that <see cref="Install"/> created. It is <see langword="null"/> before the install and when the module did not install.
    /// </summary>
    public LinuxDrmSyncobjManager? Manager { get; private set; }

    /// <summary>
    /// Decides whether the global is advertised. It returns <see langword="true"/> only when an <see cref="IDrmSyncDevice"/> is registered.
    /// </summary>
    /// <param name="services">The registry after all defaults are seeded.</param>
    /// <returns><see langword="true"/> when a sync device exists.</returns>
    public bool ShouldInstall(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Find<IDrmSyncDevice>() is not null;
    }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new LinuxDrmSyncobjManager(
            services.Display,
            services.Require<CompositorGlobal>(),
            services.Require<IDrmSyncDevice>());
        services.Use(Manager);
        services.UseDefault<IExplicitSyncDiagnostics>(Manager);
        return Manager;
    }
}
