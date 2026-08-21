using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxDrmSyncobjModule : IProtocolModule
{
    public string WireInterface => "wp_linux_drm_syncobj_manager_v1";

    public int Version => LinuxDrmSyncobjManager.Version;

    public IReadOnlyList<Type> Capabilities => [typeof(IDrmSyncDevice)];

    public LinuxDrmSyncobjManager? Manager { get; private set; }

    public bool ShouldInstall(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Find<IDrmSyncDevice>() is not null;
    }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Manager = new LinuxDrmSyncobjManager(
            services.Display,
            services.Require<CompositorGlobal>(),
            services.Require<IDrmSyncDevice>());
        services.Use(Manager);
        return Manager;
    }
}
