namespace Basin.Capabilities;

/// <summary>
/// Sets gamma ramps on an output, for example for night light tools.
/// </summary>
public interface IOutputGamma
{
    /// <summary>
    /// Gives the number of entries in each ramp that the output takes.
    /// </summary>
    /// <param name="output">The output to check.</param>
    /// <returns>The ramp size, or 0 when the output has no gamma control.</returns>
    uint RampSize(IOutput output);

    /// <summary>
    /// Applies gamma ramps to an output.
    /// </summary>
    /// <param name="output">The output to change.</param>
    /// <param name="ramps">The red, green and blue ramps, each with <see cref="RampSize"/> entries.</param>
    /// <returns><see langword="true"/> when the ramps are in effect.</returns>
    bool Apply(IOutput output, in OutputGammaRamps ramps);

    /// <summary>
    /// Removes the ramps that <see cref="Apply"/> set and restores the default gamma of the output.
    /// </summary>
    /// <param name="output">The output to reset.</param>
    /// <returns><see langword="true"/> when the default gamma is in effect.</returns>
    bool Reset(IOutput output);
}
