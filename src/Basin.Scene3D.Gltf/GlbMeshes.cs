using System.Numerics;
using Basin.Scene3D;

namespace Basin.Scene3D.Gltf;

/// <summary>
/// Bakes the rigid meshes of a glTF file into <see cref="WorldMesh"/> values.
/// </summary>
public static class GlbMeshes
{
    /// <summary>
    /// Bakes every mesh without a skin under the scene roots into one mesh, each at its node's world matrix.
    /// </summary>
    /// <param name="document">The parsed file.</param>
    /// <param name="hidden">The names of nodes to skip. A skipped node also skips its children.</param>
    /// <param name="image">The texture of the result, or <see langword="null"/>.</param>
    /// <returns>A new mesh in the file's world space.</returns>
    /// <exception cref="NotSupportedException">A primitive is not a triangle list, or an accessor has a type the reader does not read.</exception>
    public static WorldMesh Flatten(GlbDocument document, IReadOnlyCollection<string> hidden, WorldImage? image)
    {
        var world = document.WorldMatrices();
        var vertices = new List<WorldVertex>();
        var indices = new List<uint>();
        foreach (var root in document.SceneRoots())
        {
            Walk(root);
        }

        return new WorldMesh([.. vertices], [.. indices], image);

        void Walk(int node)
        {
            var info = document.Nodes[node];
            if (info.Name is { } name && hidden.Contains(name))
            {
                return;
            }

            if (info.Mesh is { } mesh && info.Skin is null)
            {
                Append(document, mesh, world[node], vertices, indices);
            }

            foreach (var child in info.Children ?? [])
            {
                Walk(child);
            }
        }
    }

    /// <summary>
    /// Appends the triangles of one glTF mesh at a transform. A primitive with no POSITION attribute is skipped. A missing normal becomes +y.
    /// </summary>
    /// <param name="document">The parsed file.</param>
    /// <param name="mesh">The index of the mesh in <see cref="GltfRoot.Meshes"/>.</param>
    /// <param name="transform">The transform from the mesh's space to the result's space.</param>
    /// <param name="vertices">The list that receives the vertices.</param>
    /// <param name="indices">The list that receives the indices, offset by the vertices already in <paramref name="vertices"/>.</param>
    /// <exception cref="NotSupportedException">A primitive is not a triangle list.</exception>
    public static void Append(
        GlbDocument document, int mesh, in Matrix4x4 transform, List<WorldVertex> vertices, List<uint> indices)
    {
        Matrix4x4.Invert(transform, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        foreach (var primitive in (document.Root.Meshes ?? [])[mesh].Primitives ?? [])
        {
            if ((primitive.Mode ?? 4) != 4)
            {
                throw new NotSupportedException($"Primitive mode {primitive.Mode} is not supported. Only triangles are read.");
            }

            if (primitive.Attributes is not { } attributes || !attributes.TryGetValue("POSITION", out var positionAccessor))
            {
                continue;
            }

            var positions = document.ReadFloats(positionAccessor);
            var normals = attributes.TryGetValue("NORMAL", out var n) ? document.ReadFloats(n) : null;
            var uvs = attributes.TryGetValue("TEXCOORD_0", out var t) ? document.ReadFloats(t) : null;
            var count = positions.Length / 3;
            var first = (uint)vertices.Count;
            for (var i = 0; i < count; i++)
            {
                var position = Vector3.Transform(
                    new Vector3(positions[i * 3], positions[(i * 3) + 1], positions[(i * 3) + 2]), transform);
                var normal = normals is null
                    ? Vector3.UnitY
                    : Vector3.Normalize(Vector3.TransformNormal(
                        new Vector3(normals[i * 3], normals[(i * 3) + 1], normals[(i * 3) + 2]), normalMatrix));
                var uv = uvs is null ? Vector2.Zero : new Vector2(uvs[i * 2], uvs[(i * 2) + 1]);
                vertices.Add(new WorldVertex(position, normal, uv));
            }

            if (primitive.Indices is { } indexAccessor)
            {
                foreach (var index in document.ReadIntegers(indexAccessor))
                {
                    indices.Add(first + index);
                }
            }
            else
            {
                for (var i = 0u; i < count; i++)
                {
                    indices.Add(first + i);
                }
            }
        }
    }
}
