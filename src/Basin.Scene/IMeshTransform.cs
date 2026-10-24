namespace Basin.Scene;

/// <summary>
/// Deforms the content of a <see cref="SceneTransform"/> as a textured triangle mesh. The scene renders the subtree to a texture and draws the texture through the mesh.
/// </summary>
public interface IMeshTransform
{
    /// <summary>
    /// Maps the bounds of the undeformed content to the bounds of the deformed result. The scene uses the result for damage and hit culling.
    /// </summary>
    /// <param name="childBounds">The undeformed content bounds, in the transform's local coordinates.</param>
    /// <returns>A box that contains every vertex that <see cref="WriteVertices"/> writes.</returns>
    Box MapBounds(in Box childBounds);

    /// <summary>
    /// Gets the number of vertices that <see cref="WriteVertices"/> will write for the given bounds.
    /// </summary>
    /// <param name="childBounds">The undeformed content bounds, in the transform's local coordinates.</param>
    /// <returns>The vertex count. It must be a multiple of 3.</returns>
    int VertexCount(in Box childBounds);

    /// <summary>
    /// Writes the mesh triangles. Positions and texture coordinates are both in the transform's local logical pixels. This runs on the frame path and must not allocate.
    /// </summary>
    /// <param name="childBounds">The undeformed content bounds, in the transform's local coordinates.</param>
    /// <param name="into">The destination, with exactly <see cref="VertexCount"/> elements.</param>
    void WriteVertices(in Box childBounds, Span<MeshVertex> into);
}
