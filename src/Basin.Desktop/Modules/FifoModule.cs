using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class FifoModule : DesktopModule<FifoManager>
{
    public override string WireInterface => "wp_fifo_manager_v1";

    public override int Version => FifoManager.Version;

    public override IReadOnlyList<Type> Drivers => [typeof(IFrameClock)];

    protected override FifoManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<CompositorGlobal>(),
            services.Require<OutputLayout>(),
            services.Loop,
            services.Require<IFrameClock>());
}
