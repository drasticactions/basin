using Basin.Capabilities;

namespace Basin.Scene;

public sealed class SceneDmabufCapture : IDmabufCapture
{
    private readonly Dictionary<IOutput, SceneOutput> _outputs = [];
    private ToplevelInfo[] _scratch = new ToplevelInfo[16];

    public IToplevelModel? Toplevels { get; set; }

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
        return !AnyExcluded()
            && _outputs.TryGetValue(output, out var sceneOutput)
            && sceneOutput.LastTarget is { } last
            && last.TryGetDmabuf(out attributes);
    }

    private bool AnyExcluded()
    {
        if (Toplevels is not { } model)
        {
            return false;
        }

        var count = model.Enumerate(_scratch);
        while (count < 0)
        {
            _scratch = new ToplevelInfo[_scratch.Length * 2];
            count = model.Enumerate(_scratch);
        }

        for (var i = 0; i < count; i++)
        {
            if ((_scratch[i].State & ToplevelState.ExcludedFromCapture) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
