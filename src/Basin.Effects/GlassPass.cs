namespace Basin.Effects;

/// <summary>
/// Names the pass shape that a glass backdrop effect uses.
/// </summary>
public enum GlassPass
{
    /// <summary>
    /// Runs the blur pyramid, then one full-resolution glass pass that reads the blur at the lensed position.
    /// </summary>
    Composite,
    /// <summary>
    /// Downsamples the backdrop once, then one shader blurs and lenses from the downsample. There is no pyramid.
    /// </summary>
    Fused,
}
