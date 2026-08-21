using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class CommitTimingModule : DesktopModule<CommitTimingManager>
{
    public override string WireInterface => "wp_commit_timing_manager_v1";

    public override int Version => CommitTimingManager.Version;

    public override IReadOnlyList<Type> Drivers => [typeof(IFrameClock)];

    protected override CommitTimingManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Loop, services.Require<IFrameClock>());
}
