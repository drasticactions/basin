using Basin.Capabilities;
using Drm;

namespace Basin.Backend.Drm;

/// <summary>
/// Sets client gamma ramps on the CRTC gamma LUT of a <see cref="DrmOutput"/>. Other outputs report no ramp.
/// </summary>
public sealed class DrmOutputGamma : IOutputGamma
{
    /// <summary>
    /// Gets or sets the ramps that every output with a matching LUT size shows when no client ramp is set. A client ramp is composed through it.
    /// </summary>
    public OutputGammaRamps? Baseline { get; set; }

    /// <summary>
    /// Gets the gamma LUT size of an output.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The number of entries per channel, or 0 when the output is not a <see cref="DrmOutput"/> or has no gamma LUT.</returns>
    public uint RampSize(IOutput output) => output is DrmOutput drm ? drm.GammaLutSize : 0;

    /// <summary>
    /// Commits client ramps to the output's gamma LUT. When <see cref="Baseline"/> matches the size, the result is the baseline applied after the client ramps.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="ramps">The client ramps, one entry per LUT entry.</param>
    /// <returns><see langword="true"/> when the commit succeeded. It is <see langword="false"/> for an output with no gamma LUT.</returns>
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

    /// <summary>
    /// Commits <see cref="Baseline"/> to the output's gamma LUT, or clears the LUT when no baseline matches the size.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns><see langword="true"/> when the commit succeeded. It is <see langword="false"/> for an output with no gamma LUT.</returns>
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

    /// <summary>
    /// Commits <see cref="Baseline"/>, the same as <see cref="Reset"/>.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns><see langword="true"/> when the commit succeeded.</returns>
    public bool ApplyBaseline(IOutput output) => Reset(output);

    private OutputGammaRamps? BaselineFor(DrmOutput output) =>
        Baseline is { } ramps && ramps.Red.Length == output.GammaLutSize ? ramps : null;
}
