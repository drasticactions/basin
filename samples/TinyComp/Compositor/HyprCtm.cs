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
    private readonly Dictionary<IOutput, double[]> _matrices = [];
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
            output.Destroyed += () =>
            {
                _stages.Remove(output);
                _matrices.Remove(output);
            };
        }

        stage.SetMatrix(rowMajor3x3);
        _matrices[output] = rowMajor3x3.ToArray();
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

        _matrices.Remove(output);
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

    public void ReleaseShader()
    {
        foreach (var (output, stage) in _stages)
        {
            if (ViewFor(output) is { Scene: { } scene })
            {
                _ = scene.RemovePostStage(stage);
            }
        }

        _stages.Clear();
        _shader?.Dispose();
        _shader = null;
        _shaderTried = false;
    }

    public void RestoreShader()
    {
        foreach (var (output, matrix) in _matrices.ToArray())
        {
            _ = SetCtm(output, matrix);
        }
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
