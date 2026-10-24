using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Scene;

namespace Basin.Desktop;

/// <summary>
/// Registers the default desktop capabilities: scene capture, DRM output control, cursor theme, color profiles, activation tokens and the bell.
/// </summary>
public sealed class DesktopServicePack : ICapabilityPack
{
    private readonly Basin.Backend.Drm.DrmBackend? _drm;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopServicePack"/> class. It creates the capture pack and the DRM pack immediately.
    /// </summary>
    /// <param name="scene">The scene that capture reads.</param>
    /// <param name="layout">The output layout that capture uses to place outputs.</param>
    /// <param name="renderer">The renderer that capture and sync object support use.</param>
    /// <param name="drm">The DRM backend, or <see langword="null"/> when the compositor does not run on DRM.</param>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public DesktopServicePack(
        Scene.Scene scene, OutputLayout layout, IRenderer renderer, Basin.Backend.Drm.DrmBackend? drm)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _drm = drm;
        Capture = new SceneCapturePack(scene, layout);
        Capture.Capture.Renderer = renderer;
        Drm = new DrmCapabilityPack(renderer, drm);
    }

    /// <summary>
    /// Gets the pack that provides screen capture from the scene.
    /// </summary>
    public SceneCapturePack Capture { get; }

    /// <summary>
    /// Gets the pack that provides gamma, power, leasing and sync object capabilities.
    /// </summary>
    public DrmCapabilityPack Drm { get; }

    /// <summary>
    /// Gets the cursor theme that the pack registers as <see cref="ICursorTheme"/>.
    /// </summary>
    public CursorImageTheme CursorTheme { get; } = new();

    /// <summary>
    /// Gets the Little CMS profile service that the pack registers as <see cref="IColorProfileService"/>.
    /// </summary>
    public Basin.Color.Lcms2ColorProfileService ColorProfiles { get; } = new();

    /// <summary>
    /// Gets or sets the bell that the pack registers. The default makes no sound. Set it before <see cref="Register(BasinServices)"/>.
    /// </summary>
    public IBell Bell { get; set; } = SilentBell.Instance;

    /// <summary>
    /// Gets or sets the activation token source that the pack registers. Set it before <see cref="Register(BasinServices)"/>.
    /// </summary>
    public IActivationTokens ActivationTokens { get; set; } = new DefaultActivationTokens();

    /// <summary>
    /// Registers every capability of the pack. Without a DRM backend, it removes the <c>wp_drm_lease_device_v1</c> global.
    /// </summary>
    /// <param name="services">The registry to add to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public void Register(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services
            .With(Capture)
            .With(Drm)
            .Use<ICursorTheme>(CursorTheme)
            .Use<IColorProfileService>(ColorProfiles)
            .Use(ActivationTokens)
            .Use(Bell);

        if (_drm is null)
        {
            services.Without("wp_drm_lease_device_v1");
        }
    }
}
