using Basin.Capabilities;
using Drm;

namespace Basin.Backend.Drm;

public sealed class DrmOutputGamma : IOutputGamma
{
    public OutputGammaRamps? Baseline { get; set; }

    public uint RampSize(IOutput output) => output is DrmOutput drm ? drm.GammaLutSize : 0;

    public bool Apply(IOutput output, in OutputGammaRamps ramps)
    {
        if (output is not DrmOutput drm || drm.GammaLutSize == 0)
        {
            return false;
        }

        using var state = new OutputState();
        state.SetGammaLut(ramps);
        return drm.Commit(state);
    }

    public bool Reset(IOutput output)
    {
        if (output is not DrmOutput drm || drm.GammaLutSize == 0)
        {
            return false;
        }

        using var state = new OutputState();
        state.SetGammaLut(BaselineFor(drm));
        return drm.Commit(state);
    }

    public bool ApplyBaseline(IOutput output) => Reset(output);

    private OutputGammaRamps? BaselineFor(DrmOutput output) =>
        Baseline is { } ramps && ramps.Red.Length == output.GammaLutSize ? ramps : null;
}
