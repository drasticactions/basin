using Pixman;

namespace Basin;

/// <summary>
/// Describes one vertex of a mesh draw.
/// </summary>
/// <param name="X">The horizontal position in target pixels.</param>
/// <param name="Y">The vertical position in target pixels.</param>
/// <param name="U">The horizontal sample point in buffer pixels.</param>
/// <param name="V">The vertical sample point in buffer pixels.</param>
/// <param name="Color">The premultiplied color that multiplies the sampled texel, or that draws alone without a texture.</param>
public readonly record struct MeshVertex(float X, float Y, float U, float V, RenderColor Color);
