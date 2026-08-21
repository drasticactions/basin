using Basin.Capabilities;

namespace Basin.Scene;

public sealed class SceneCapturePack : ICapabilityPack
{
    public SceneCapturePack(Scene scene, OutputLayout layout)
    {
        Capture = new SceneScreenCapture(scene, layout);
        DmabufCapture = new SceneDmabufCapture();
    }

    public SceneScreenCapture Capture { get; }

    public SceneDmabufCapture DmabufCapture { get; }

    public void Register(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Use<IScreenCapture>(Capture).Use<IDmabufCapture>(DmabufCapture);
    }
}
