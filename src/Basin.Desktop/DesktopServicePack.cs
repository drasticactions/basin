using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Scene;

namespace Basin.Desktop;

public sealed class DesktopServicePack : ICapabilityPack
{
    private readonly Basin.Backend.Drm.DrmBackend? _drm;

    public DesktopServicePack(
        Scene.Scene scene, OutputLayout layout, IRenderer renderer, Basin.Backend.Drm.DrmBackend? drm)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _drm = drm;
        Capture = new SceneCapturePack(scene, layout);
        Capture.Capture.Renderer = renderer;
        Drm = new DrmCapabilityPack(renderer, drm);
    }

    public SceneCapturePack Capture { get; }

    public DrmCapabilityPack Drm { get; }

    public CursorImageTheme CursorTheme { get; } = new();

    public Basin.Color.Lcms2ColorProfileService ColorProfiles { get; } = new();

    public IBell Bell { get; set; } = SilentBell.Instance;

    public IActivationTokens ActivationTokens { get; set; } = new DefaultActivationTokens();

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
