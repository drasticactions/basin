namespace Basin.Scene;

/// <summary>
/// Supplies the triangles that a <see cref="SceneMesh"/> draws, colored or textured with the mesh's sprite buffer.
/// </summary>
public interface IMeshSource
{
    /// <summary>
    /// Gets the number of vertices that <see cref="WriteVertices"/> will write for the given bounds.
    /// </summary>
    /// <param name="bounds">The mesh node's <see cref="SceneMesh.Bounds"/>.</param>
    /// <returns>The vertex count. It must be a multiple of 3.</returns>
    int VertexCount(in Box bounds);

    /// <summary>
    /// Writes the mesh triangles. Positions are in the mesh node's local logical pixels. Texture coordinates are in sprite buffer pixels. This must not allocate.
    /// </summary>
    /// <param name="bounds">The mesh node's <see cref="SceneMesh.Bounds"/>.</param>
    /// <param name="into">The destination, with exactly <see cref="VertexCount"/> elements.</param>
    void WriteVertices(in Box bounds, Span<MeshVertex> into);
}
