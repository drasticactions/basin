using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_commit_timing_manager_v1</c> global through a <see cref="CommitTimingManager"/>. It requires an <see cref="IFrameClock"/> to release timed commits.
/// </summary>
public sealed class CommitTimingModule : DesktopModule<CommitTimingManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_commit_timing_manager_v1";

    /// <inheritdoc/>
    public override int Version => CommitTimingManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(IFrameClock)];

    /// <inheritdoc/>
    protected override CommitTimingManager Create(BasinServices services) =>
        new(services.Display, services.Require<CompositorGlobal>(), services.Loop, services.Require<IFrameClock>());
}
