using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>org_kde_kwin_server_decoration_manager</c> global through a <see cref="KdeServerDecorationManager"/>. It reads <see cref="IFrameRenderer"/> only to choose the default decoration mode.
/// </summary>
public sealed class KdeServerDecorationModule : DesktopModule<KdeServerDecorationManager>
{
    private readonly KdeServerDecorationManager.DecorationMode? _defaultMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="KdeServerDecorationModule"/> class.
    /// </summary>
    /// <param name="defaultMode">The mode to announce to clients. When it is <see langword="null"/>, the mode is server-side if an <see cref="IFrameRenderer"/> is registered and client-side if not.</param>
    public KdeServerDecorationModule(KdeServerDecorationManager.DecorationMode? defaultMode = null) =>
        _defaultMode = defaultMode;

    /// <inheritdoc/>
    public override string WireInterface => "org_kde_kwin_server_decoration_manager";

    /// <inheritdoc/>
    public override int Version => KdeServerDecorationManager.Version;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IFrameRenderer)];

    /// <inheritdoc/>
    protected override KdeServerDecorationManager Create(BasinServices services) =>
        new(
            services.Display,
            services.Require<CompositorGlobal>(),
            _defaultMode ?? (services.Find<IFrameRenderer>() is null
                ? KdeServerDecorationManager.DecorationMode.Client
                : KdeServerDecorationManager.DecorationMode.Server));
}
