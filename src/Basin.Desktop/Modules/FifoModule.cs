using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>wp_fifo_manager_v1</c> global through a <see cref="FifoManager"/>. It requires an <see cref="IFrameClock"/> to release queued commits.
/// </summary>
public sealed class FifoModule : DesktopModule<FifoManager>
{
    /// <inheritdoc/>
    public override string WireInterface => "wp_fifo_manager_v1";

    /// <inheritdoc/>
    public override int Version => FifoManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(IFrameClock)];

    /// <inheritdoc/>
    protected override FifoManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<CompositorGlobal>(),
            services.Require<OutputLayout>(),
            services.Loop,
            services.Require<IFrameClock>());
}
