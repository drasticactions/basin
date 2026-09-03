using Basin.Capabilities;
using Basin.Capabilities.Defaults;

namespace Basin.Scene;

public sealed class SceneCapturePack : ICapabilityPack
{
    public SceneCapturePack(Scene scene, OutputLayout layout, ISurfaceAppearance? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Index = new ToplevelSceneIndex();
        Stack = new SceneToplevelStack(scene, Index);
        Capture = new SceneScreenCapture(scene, layout) { Index = Index };
        DmabufCapture = new SceneDmabufCapture();
        Appearance = appearance ?? new DefaultSurfaceAppearance();
        scene.Appearance = Appearance;
    }

    public ISurfaceAppearance Appearance { get; }

    public ToplevelSceneIndex Index { get; }

    public SceneToplevelStack Stack { get; }

    public SceneScreenCapture Capture { get; }

    public SceneDmabufCapture DmabufCapture { get; }

    public ToplevelCaptureIndexObserver Attach(
        Capabilities.IToplevelModel toplevels,
        Func<Surface, ToplevelCaptureTrees?> resolve)
    {
        ArgumentNullException.ThrowIfNull(toplevels);
        Capture.Toplevels = toplevels;
        DmabufCapture.Toplevels = toplevels;
        var observer = new ToplevelCaptureIndexObserver(toplevels, Index, Stack, resolve);
        toplevels.AddObserver(observer);
        return observer;
    }

    public void Register(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services
            .Use<IScreenCapture>(Capture)
            .Use<IDmabufCapture>(DmabufCapture)
            .Use(Appearance)
            .UseDefault<IToplevelStack>(Stack);
    }
}
