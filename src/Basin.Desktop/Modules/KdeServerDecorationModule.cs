using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

public sealed class KdeServerDecorationModule : DesktopModule<KdeServerDecorationManager>
{
    private readonly KdeServerDecorationManager.DecorationMode? _defaultMode;

    public KdeServerDecorationModule(KdeServerDecorationManager.DecorationMode? defaultMode = null) =>
        _defaultMode = defaultMode;

    public override string WireInterface => "org_kde_kwin_server_decoration_manager";

    public override int Version => KdeServerDecorationManager.Version;

    public override IReadOnlyList<Type> Capabilities => [typeof(IFrameRenderer)];

    protected override KdeServerDecorationManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<CompositorGlobal>(),
            _defaultMode ?? (services.Find<IFrameRenderer>() is null
                ? KdeServerDecorationManager.DecorationMode.Client
                : KdeServerDecorationManager.DecorationMode.Server));
}
