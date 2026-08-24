using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class FakeInputModule : PlasmaModule<FakeInputManager>
{
    public override string WireInterface => "org_kde_kwin_fake_input";

    public override int Version => FakeInputManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IFakeInputAuthority), typeof(IInputSink)];

    protected override FakeInputManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Find<IFakeInputAuthority>(),
            services.Find<IInputSink>(),
            services.Find<Basin.Seat.Seat>(),
            services.Find<OutputLayout>());
}
