namespace Basin.Scene3D;

/// <summary>
/// The tunable limits of a 3D pass. The consumer can change them between frames.
/// </summary>
public sealed class WorldRenderSettings
{
    /// <summary>
    /// Gets or sets how many portal levels deep the pass draws. Each level is a view through a portal. The default is 3.
    /// </summary>
    public int PortalDepth { get; set; } = 3;

    /// <summary>
    /// Gets or sets how many doorways deep the visibility walk goes from the eye's room. The default is 4.
    /// </summary>
    public int RoomDepth { get; set; } = 4;

    /// <summary>
    /// Gets or sets the maximum number of lights, nearest to the eye first, that light one level. The pass caps it at 8.
    /// </summary>
    public int MaxLights { get; set; } = 8;

    /// <summary>
    /// Gets or sets the maximum anisotropic filtering for window and atlas textures. Both passes read it each frame. The device's own limit caps it.
    /// </summary>
    public float Anisotropy { get; set; } = 8;

    /// <summary>
    /// Gets or sets the texel-to-pixel ratio above which a pass samples a mipmapped copy of a window texture. At or below it, the pass samples the texture directly.
    /// </summary>
    public float DirectSampleMax { get; set; } = 1.2f;
}
