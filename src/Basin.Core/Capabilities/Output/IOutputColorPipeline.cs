namespace Basin.Capabilities;

/// <summary>
/// The fixed-function color stages of an output's display hardware. The color driver can route a profile through
/// them instead of baking it into the render.
/// </summary>
public interface IOutputColorPipeline
{
    /// <summary>
    /// Gets the number of entries in the degamma lookup table, or 0 when the hardware has none.
    /// </summary>
    uint DegammaLutSize { get; }

    /// <summary>
    /// Gets the number of entries in the gamma lookup table, or 0 when the hardware has none.
    /// </summary>
    uint GammaLutSize { get; }

    /// <summary>
    /// Gets a value indicating whether the hardware has a color transformation matrix stage.
    /// </summary>
    bool SupportsCtm { get; }
}
