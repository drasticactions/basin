using Basin.Capabilities;

namespace Basin.Scene;

public sealed class SceneDmabufCapture : IDmabufCapture
{
    private readonly Dictionary<IOutput, SceneOutput> _outputs = [];

    public void Track(IOutput output, SceneOutput sceneOutput)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(sceneOutput);
        _outputs[output] = sceneOutput;
    }

    public void Forget(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _outputs.Remove(output);
    }

    public bool TryCurrentFrame(IOutput output, out DmabufAttributes attributes)
    {
        attributes = default;
        return _outputs.TryGetValue(output, out var sceneOutput)
            && sceneOutput.LastTarget is { } last
            && last.TryGetDmabuf(out attributes);
    }
}
