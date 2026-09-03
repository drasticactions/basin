using Basin;
using Basin.Backend.Drm;
using Basin.Capabilities;
using Basin.Effects;
using Basin.Host;

namespace TinyComp;

internal sealed class HyprCtm : ICtmControl
{
    private readonly TinyComp _comp;
    private readonly Dictionary<IOutput, CtmStage> _stages = [];
    private IPixelShader? _shader;
    private bool _shaderTried;

    public HyprCtm(TinyComp comp) => _comp = comp;

    public bool SupportsCtm(IOutput output) => true;

    public bool SetCtm(IOutput output, ReadOnlySpan<double> rowMajor3x3)
    {
        if (output is DrmOutput { SupportsCtm: true } drm)
        {
            using var state = new OutputState();
            state.SetCtm(rowMajor3x3.ToArray());
            return drm.Commit(state);
        }

        if (ViewFor(output) is not { Scene: { } scene } view)
        {
            return false;
        }

        if (!_stages.TryGetValue(output, out var stage))
        {
            if (Shader() is not { } shader)
            {
                return false;
            }

            stage = new CtmStage(shader);
            _stages[output] = stage;
            scene.AddPostStage(stage);
            output.Destroyed += () => _stages.Remove(output);
        }

        stage.SetMatrix(rowMajor3x3);
        view.Scheduler?.ScheduleRepaint();
        return true;
    }

    public bool ResetCtm(IOutput output)
    {
        if (output is DrmOutput { SupportsCtm: true } drm)
        {
            using var state = new OutputState();
            state.SetCtm(null);
            return drm.Commit(state);
        }

        if (!_stages.Remove(output, out var stage))
        {
            return true;
        }

        if (ViewFor(output) is { Scene: { } scene } view)
        {
            _ = scene.RemovePostStage(stage);
            view.Scheduler?.ScheduleRepaint();
        }

        return true;
    }

    private OutputView? ViewFor(IOutput output) => _comp.ViewOf(output);

    private IPixelShader? Shader()
    {
        if (_shaderTried)
        {
            return _shader;
        }

        _shaderTried = true;
        _shader = _comp.CompilePostShader(CtmShader.Source, CtmShader.Uniforms);
        if (_shader is null)
        {
            _comp.WarnNoCtmShader();
        }

        return _shader;
    }
}
