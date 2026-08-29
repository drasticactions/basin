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

        var applied = ramps;
        if (BaselineFor(drm) is { } baseline && ramps.Red.Length == baseline.Red.Length)
        {
            var size = baseline.Red.Length;
            var composed = new OutputGammaRamps(new ushort[size], new ushort[size], new ushort[size]);
            for (var i = 0; i < size; i++)
            {
                composed.Red[i] = baseline.Red[RampIndex(ramps.Red[i], size)];
                composed.Green[i] = baseline.Green[RampIndex(ramps.Green[i], size)];
                composed.Blue[i] = baseline.Blue[RampIndex(ramps.Blue[i], size)];
            }

            applied = composed;
        }

        using var state = new OutputState();
        state.SetGammaLut(applied);
        return drm.Commit(state);
    }

    private static int RampIndex(ushort value, int size) =>
        (int)Math.Round(value * (double)(size - 1) / ushort.MaxValue);

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
