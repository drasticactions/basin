using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class ScreencastModule : PlasmaModule<ScreencastManager>
{
    public override string WireInterface => "zkde_screencast_unstable_v1";

    public override int Version => ScreencastManager.Version;

    public override IReadOnlyList<Type> Capabilities =>
        [typeof(IScreencastPublisher), typeof(IVirtualOutputFactory), typeof(IToplevelModel),
         typeof(IScreenCapture), typeof(IOutputSet)];

    protected override ScreencastManager Create(BasinServices services) =>
        new(services.Display,
            services.Find<IScreencastPublisher>(),
            services.Find<IVirtualOutputFactory>(),
            services.Find<IToplevelModel>(),
            services.Find<IScreenCapture>(),
            services.Find<IOutputSet>());
}
