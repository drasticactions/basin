namespace Basin.Scene3D;

/// <summary>
/// An immutable triangle list with an optional texture. A pass uploads it one time and caches it by reference.
/// </summary>
public sealed class WorldMesh
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorldMesh"/> class and computes its bounds. The mesh keeps the arrays and does not copy them.
    /// </summary>
    /// <param name="vertices">The vertices in model space.</param>
    /// <param name="indices">The vertex indices, three for each triangle.</param>
    /// <param name="image">The texture, or <see langword="null"/> for an untextured mesh.</param>
    /// <exception cref="ArgumentNullException"><paramref name="vertices"/> or <paramref name="indices"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The index count is not a multiple of 3.</exception>
    public WorldMesh(WorldVertex[] vertices, uint[] indices, WorldImage? image)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException("Indices must form whole triangles.", nameof(indices));
        }

        Vertices = vertices;
        Indices = indices;
        Image = image;
        var bounds = WorldBounds.Empty;
        foreach (var vertex in vertices)
        {
            bounds = bounds.Include(vertex.Position);
        }

        Bounds = bounds;
    }

    /// <summary>
    /// Gets the vertices in model space. A pass does not see a change after its first upload.
    /// </summary>
    public WorldVertex[] Vertices { get; }

    /// <summary>
    /// Gets the vertex indices, three for each triangle.
    /// </summary>
    public uint[] Indices { get; }

    /// <summary>
    /// Gets the texture, or <see langword="null"/> for an untextured mesh.
    /// </summary>
    public WorldImage? Image { get; }

    /// <summary>
    /// Gets the model-space box round the vertices, computed at construction.
    /// </summary>
    public WorldBounds Bounds { get; }

    /// <summary>
    /// Gets the number of triangles.
    /// </summary>
    public int TriangleCount => Indices.Length / 3;
}
