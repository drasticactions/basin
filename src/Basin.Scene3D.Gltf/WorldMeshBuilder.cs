using System.Numerics;
using Basin.Scene3D;

namespace Basin.Scene3D.Gltf;

/// <summary>
/// Merges meshes at transforms into one <see cref="WorldMesh"/>, so a room or a prop is one draw.
/// </summary>
public sealed class WorldMeshBuilder
{
    private readonly List<WorldVertex> _vertices = [];
    private readonly List<uint> _indices = [];

    /// <summary>
    /// Gets the number of triangles added so far.
    /// </summary>
    public int Triangles => _indices.Count / 3;

    /// <summary>
    /// Appends a copy of a mesh at a transform. The normals take the inverse transpose, so a nonuniform scale keeps them correct.
    /// </summary>
    /// <param name="mesh">The mesh to copy.</param>
    /// <param name="transform">The transform from the mesh's space to the result's space.</param>
    public void Add(WorldMesh mesh, in Matrix4x4 transform)
    {
        Matrix4x4.Invert(transform, out var inverse);
        var normals = Matrix4x4.Transpose(inverse);
        var first = (uint)_vertices.Count;
        foreach (var vertex in mesh.Vertices)
        {
            _vertices.Add(new WorldVertex(
                Vector3.Transform(vertex.Position, transform),
                Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, normals)),
                vertex.Uv));
        }

        foreach (var index in mesh.Indices)
        {
            _indices.Add(first + index);
        }
    }

    /// <summary>
    /// Builds a mesh from everything added so far. The builder keeps its contents, so later calls can add more.
    /// </summary>
    /// <param name="image">The texture of the result, or <see langword="null"/>.</param>
    /// <returns>A new mesh with copies of the vertex and index lists.</returns>
    public WorldMesh Build(WorldImage? image) => new([.. _vertices], [.. _indices], image);
}
