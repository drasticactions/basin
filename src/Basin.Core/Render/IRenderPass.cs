using Pixman;

namespace Basin;

/// <summary>
/// Records draws into one target buffer. Operations draw in call order. Coordinates are target pixels.
/// </summary>
public interface IRenderPass
{
    /// <summary>
    /// Blends a solid rectangle over the target.
    /// </summary>
    /// <param name="color">The premultiplied color.</param>
    /// <param name="box">The rectangle in target pixels.</param>
    /// <param name="clip">The region that limits the draw, or <see langword="null"/> for no clip.</param>
    void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null);

    /// <summary>
    /// Replaces every pixel of the target with a color, without blending. It is the only operation that can make a target transparent.
    /// </summary>
    /// <param name="color">The premultiplied color.</param>
    void Clear(in RenderColor color);

    /// <summary>
    /// Draws a texture. A texture from another renderer throws.
    /// </summary>
    /// <param name="texture">A texture that this renderer imported.</param>
    /// <param name="options">The source box, destination, transform and other draw options.</param>
    void AddTexture(ITexture texture, in TextureRenderOptions options);

    /// <summary>
    /// Draws a list of triangles without an index buffer. A vertex count that is not a multiple of three throws.
    /// </summary>
    /// <param name="texture">The texture that the vertices sample, or <see langword="null"/> to draw the vertex colors alone.</param>
    /// <param name="vertices">The vertices, three for each triangle.</param>
    /// <param name="options">The blend and the clip.</param>
    void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options);

    /// <summary>
    /// Applies a backdrop effect to the pixels already drawn inside a box. The default does nothing, so the backdrop stays as drawn.
    /// </summary>
    /// <param name="effect">An effect of the interface that this renderer accepts.</param>
    /// <param name="bounds">The region of the effect in target pixels.</param>
    /// <param name="clip">The region that limits the result, or <see langword="null"/> for no clip.</param>
    /// <param name="key">The key under which the effect keeps state, or <see langword="null"/>.</param>
    void AddBackdropEffect(IBackdropEffect effect, in Box bounds, PixmanRegion32? clip = null, object? key = null)
    {
    }

    /// <summary>
    /// Applies a backdrop effect that also needs the full extent of its node, for example a shape that a clipped bounds box cuts. The default ignores the extent.
    /// </summary>
    /// <param name="effect">An effect of the interface that this renderer accepts.</param>
    /// <param name="bounds">The region of the effect in target pixels, clipped to the target.</param>
    /// <param name="extent">The whole box of the node in target pixels, before the clip.</param>
    /// <param name="clip">The region that limits the result, or <see langword="null"/> for no clip.</param>
    /// <param name="key">The key under which the effect keeps state, or <see langword="null"/>.</param>
    void AddBackdropEffect(IBackdropEffect effect, in Box bounds, in Box extent, PixmanRegion32? clip, object? key) =>
        AddBackdropEffect(effect, bounds, clip, key);

    /// <summary>
    /// Applies a backdrop effect to a node that a mesh deforms, so the effect can bend with the node. The default ignores the mesh.
    /// </summary>
    /// <param name="effect">An effect of the interface that this renderer accepts.</param>
    /// <param name="bounds">The region of the effect in target pixels.</param>
    /// <param name="extent">The whole box of the node in target pixels.</param>
    /// <param name="clip">The region that limits the result, or <see langword="null"/> for no clip.</param>
    /// <param name="key">The key under which the effect keeps state, or <see langword="null"/>.</param>
    /// <param name="mesh">The triangles of the node, with deformed positions in target pixels and undeformed node-local positions as texture coordinates.</param>
    void AddDeformedBackdropEffect(
        IBackdropEffect effect, in Box bounds, in Box extent, PixmanRegion32? clip, object? key, ReadOnlySpan<MeshVertex> mesh) =>
        AddBackdropEffect(effect, bounds, extent, clip, key);

    /// <summary>
    /// Fills a box with the output of a pixel shader. The default does nothing.
    /// </summary>
    /// <param name="shader">A shader that this renderer compiled.</param>
    /// <param name="options">The destination, opacity and clip.</param>
    void AddShader(IPixelShader shader, in ShaderRenderOptions options)
    {
    }

    /// <summary>
    /// Runs a frame filter that reads a source texture and writes the whole target.
    /// </summary>
    /// <param name="filter">A filter of the interface that this renderer accepts.</param>
    /// <param name="source">The composed frame.</param>
    /// <param name="options">The per-frame values for the filter.</param>
    /// <returns><see langword="false"/> when the target was not written, which is the default. The caller then draws the source itself.</returns>
    bool AddFrameFilter(IFrameFilter filter, ITexture source, in FrameFilterOptions options) => false;

    /// <summary>
    /// Ends the pass and submits its work. With a signal fence on a <see cref="RenderFencePrecision.Context"/> renderer, the fence is signaled when the call returns.
    /// </summary>
    /// <returns><see langword="false"/> when the device did not take the work, so the target content is not defined.</returns>
    bool Submit();
}
