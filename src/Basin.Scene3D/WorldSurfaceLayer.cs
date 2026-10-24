namespace Basin.Scene3D;

/// <summary>
/// One surface drawn on a <see cref="WorldWindow"/>, in the main surface's logical coordinates.
/// </summary>
public struct WorldSurfaceLayer
{
    /// <summary>
    /// The surface that this layer shows, or <see langword="null"/> for content that has no surface.
    /// </summary>
    public Surface? Surface;
    /// <summary>
    /// The texture to sample. A layer with no texture draws nothing.
    /// </summary>
    public ITexture? Texture;
    /// <summary>
    /// The left edge of the layer, in the main surface's logical pixels.
    /// </summary>
    public float X;
    /// <summary>
    /// The top edge of the layer, in the main surface's logical pixels.
    /// </summary>
    public float Y;
    /// <summary>
    /// The width of the layer, in logical pixels.
    /// </summary>
    public float Width;
    /// <summary>
    /// The height of the layer, in logical pixels.
    /// </summary>
    public float Height;
    /// <summary>
    /// The part of the texture to sample, in buffer pixels. An empty box samples the whole texture.
    /// </summary>
    public FBox Source;
    /// <summary>
    /// The stacking level. Level 0 is the window, and popups and subsurfaces are higher. Each level sits <see cref="WorldPicker.LayerLift"/> in front of the level below.
    /// </summary>
    public int Level;
    /// <summary>
    /// A value indicating whether the pass can ignore the texture's alpha.
    /// </summary>
    public bool Opaque;
    /// <summary>
    /// The content version of the buffer. A pass refreshes its mip copy when this value or the texture changes.
    /// </summary>
    public int ContentVersion;
    /// <summary>
    /// A value indicating whether a pick on this layer gives its <see cref="Surface"/> as the hit.
    /// </summary>
    public bool Input;
}
