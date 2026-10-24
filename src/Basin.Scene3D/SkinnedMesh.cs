namespace Basin.Scene3D;

/// <summary>
/// An immutable triangle list skinned to a <see cref="Skeleton"/>.
/// </summary>
public sealed class SkinnedMesh
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkinnedMesh"/> class. It keeps the arrays and does not copy them.
    /// </summary>
    /// <param name="vertices">The vertices in the bind pose.</param>
    /// <param name="indices">The vertex indices, three for each triangle.</param>
    /// <param name="image">The texture, or <see langword="null"/> for an untextured mesh.</param>
    /// <exception cref="ArgumentNullException"><paramref name="vertices"/> or <paramref name="indices"/> is <see langword="null"/>.</exception>
    public SkinnedMesh(SkinnedVertex[] vertices, uint[] indices, WorldImage? image)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        Vertices = vertices;
        Indices = indices;
        Image = image;
    }

    /// <summary>
    /// Gets the vertices in the bind pose.
    /// </summary>
    public SkinnedVertex[] Vertices { get; }

    /// <summary>
    /// Gets the vertex indices, three for each triangle.
    /// </summary>
    public uint[] Indices { get; }

    /// <summary>
    /// Gets the texture, or <see langword="null"/> for an untextured mesh.
    /// </summary>
    public WorldImage? Image { get; }

    /// <summary>
    /// Gets the number of triangles.
    /// </summary>
    public int TriangleCount => Indices.Length / 3;
}
