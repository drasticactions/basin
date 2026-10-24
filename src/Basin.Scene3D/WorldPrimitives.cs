using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// Shared unit meshes that the passes scale by a model matrix, and the frame moulding builder.
/// </summary>
public static class WorldPrimitives
{
    /// <summary>
    /// Gets a unit square in the xy plane, centered on the origin and facing +z. Its texture coordinates put v = 0 at the top.
    /// </summary>
    public static WorldMesh Quad { get; } = Build(builder => builder.Face(
        new Vector3(0, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0, 0.5f, 0), frontUv: true));

    /// <summary>
    /// Gets the four sides of a unit box that runs from z = 0 back to z = -1, with no front or back.
    /// </summary>
    public static WorldMesh SlabEdges { get; } = Build(builder =>
    {
        builder.Face(new Vector3(0.5f, 0, -0.5f), new Vector3(0, 0, -0.5f), new Vector3(0, 0.5f, 0));
        builder.Face(new Vector3(-0.5f, 0, -0.5f), new Vector3(0, 0, 0.5f), new Vector3(0, 0.5f, 0));
        builder.Face(new Vector3(0, 0.5f, -0.5f), new Vector3(0.5f, 0, 0), new Vector3(0, 0, -0.5f));
        builder.Face(new Vector3(0, -0.5f, -0.5f), new Vector3(0.5f, 0, 0), new Vector3(0, 0, 0.5f));
    });

    /// <summary>
    /// Gets the back face of the unit slab, a unit square at z = -1 that faces -z.
    /// </summary>
    public static WorldMesh SlabBack { get; } = Build(builder =>
        builder.Face(new Vector3(0, 0, -1), new Vector3(-0.5f, 0, 0), new Vector3(0, 0.5f, 0)));

    /// <summary>
    /// Gets an octahedron one meter across, centered on the origin, with flat normals. The passes draw lights with it.
    /// </summary>
    public static WorldMesh Flame { get; } = BuildOctahedron();

    /// <summary>
    /// Builds a picture-frame moulding round a canvas in the window's local space. It sweeps one profile round the canvas with mitred corners and a normal for each face.
    /// </summary>
    /// <param name="innerWidth">The canvas width in meters.</param>
    /// <param name="innerHeight">The canvas height in meters.</param>
    /// <param name="rail">The width of each rail in meters. It must be positive.</param>
    /// <param name="depth">The distance in meters from the front plane back to the wall. It must be positive.</param>
    /// <param name="recess">The distance in meters that the canvas sits behind the front plane.</param>
    /// <param name="uvRect">The swatch in <paramref name="image"/>: X and Y are its top-left texture coordinate and W its height. Each profile point takes one row of it.</param>
    /// <param name="image">The texture that holds the swatch, or <see langword="null"/> for an untextured moulding.</param>
    /// <returns>A new mesh. The caller caches it for the canvas size.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rail"/> or <paramref name="depth"/> is not positive.</exception>
    public static WorldMesh Moulding(
        float innerWidth, float innerHeight, float rail, float depth, float recess, Vector4 uvRect, WorldImage? image)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(rail, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(depth, 0);
        Span<Vector2> profile =
        [
            new(0, -recess),
            new(0.10f * rail, -0.2f * recess),
            new(0.30f * rail, -0.2f * recess),
            new(0.55f * rail, 0),
            new(0.70f * rail, 0),
            new(1.00f * rail, -0.45f * depth),
            new(1.00f * rail, -depth),
        ];
        ReadOnlySpan<float> shade = [0.75f, 0.55f, 0.5f, 0.15f, 0.1f, 0.45f, 0.9f];
        var halfWidth = innerWidth * 0.5f;
        var halfHeight = innerHeight * 0.5f;
        var vertices = new WorldVertex[4 * 4 * (profile.Length - 1)];
        var indices = new uint[6 * 4 * (profile.Length - 1)];
        var v = 0;
        var i = 0;
        for (var side = 0; side < 4; side++)
        {
            var (outward, along, edge, length) = side switch
            {
                0 => (Vector2.UnitY, Vector2.UnitX, halfHeight, halfWidth),
                1 => (-Vector2.UnitY, Vector2.UnitX, halfHeight, halfWidth),
                2 => (Vector2.UnitX, Vector2.UnitY, halfWidth, halfHeight),
                _ => (-Vector2.UnitX, Vector2.UnitY, halfWidth, halfHeight),
            };
            for (var k = 0; k + 1 < profile.Length; k++)
            {
                var a = profile[k];
                var b = profile[k + 1];
                var slope = b - a;
                var flat = Vector2.Normalize(new Vector2(-slope.Y, slope.X));
                var normal = Vector3.Normalize(new Vector3(outward * flat.X, flat.Y));
                var p0 = Point(outward, along, edge, length, a, -1);
                var p1 = Point(outward, along, edge, length, a, 1);
                var p2 = Point(outward, along, edge, length, b, 1);
                var p3 = Point(outward, along, edge, length, b, -1);
                var uvA = new Vector2(uvRect.X, uvRect.Y + (shade[k] * uvRect.W));
                var uvB = new Vector2(uvRect.X, uvRect.Y + (shade[k + 1] * uvRect.W));
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) < 0)
                {
                    (p0, p1) = (p1, p0);
                    (p2, p3) = (p3, p2);
                }

                var first = (uint)v;
                vertices[v++] = new WorldVertex(p0, normal, uvA);
                vertices[v++] = new WorldVertex(p1, normal, uvA);
                vertices[v++] = new WorldVertex(p2, normal, uvB);
                vertices[v++] = new WorldVertex(p3, normal, uvB);
                indices[i++] = first;
                indices[i++] = first + 1;
                indices[i++] = first + 2;
                indices[i++] = first;
                indices[i++] = first + 2;
                indices[i++] = first + 3;
            }
        }

        return new WorldMesh(vertices, indices, image);

        static Vector3 Point(Vector2 outward, Vector2 along, float edge, float length, Vector2 profile, float end)
        {
            var planar = (outward * (edge + profile.X)) + (along * end * (length + profile.X));
            return new Vector3(planar, profile.Y);
        }
    }

    private static WorldMesh Build(Action<Builder> faces)
    {
        var builder = new Builder();
        faces(builder);
        return new WorldMesh([.. builder.Vertices], [.. builder.Indices], null);
    }

    private static WorldMesh BuildOctahedron()
    {
        var points = new[]
        {
            new Vector3(0.5f, 0, 0), new Vector3(-0.5f, 0, 0), new Vector3(0, 0.5f, 0),
            new Vector3(0, -0.5f, 0), new Vector3(0, 0, 0.5f), new Vector3(0, 0, -0.5f),
        };
        int[] faces = [0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 4, 3, 0, 1, 3, 4, 5, 3, 1, 0, 3, 5];
        var vertices = new WorldVertex[faces.Length];
        var indices = new uint[faces.Length];
        for (var i = 0; i < faces.Length; i += 3)
        {
            var a = points[faces[i]];
            var b = points[faces[i + 1]];
            var c = points[faces[i + 2]];
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            vertices[i] = new WorldVertex(a, normal, Vector2.Zero);
            vertices[i + 1] = new WorldVertex(b, normal, Vector2.Zero);
            vertices[i + 2] = new WorldVertex(c, normal, Vector2.Zero);
            indices[i] = (uint)i;
            indices[i + 1] = (uint)(i + 1);
            indices[i + 2] = (uint)(i + 2);
        }

        return new WorldMesh(vertices, indices, null);
    }

    private sealed class Builder
    {
        public List<WorldVertex> Vertices { get; } = [];

        public List<uint> Indices { get; } = [];

        public void Face(Vector3 center, Vector3 u, Vector3 v, bool frontUv = false)
        {
            var normal = Vector3.Normalize(Vector3.Cross(u, v));
            var first = (uint)Vertices.Count;
            Vertices.Add(new WorldVertex(center - u - v, normal, frontUv ? new Vector2(0, 1) : Vector2.Zero));
            Vertices.Add(new WorldVertex(center + u - v, normal, frontUv ? new Vector2(1, 1) : Vector2.Zero));
            Vertices.Add(new WorldVertex(center + u + v, normal, frontUv ? new Vector2(1, 0) : Vector2.Zero));
            Vertices.Add(new WorldVertex(center - u + v, normal, frontUv ? new Vector2(0, 0) : Vector2.Zero));
            Indices.Add(first);
            Indices.Add(first + 1);
            Indices.Add(first + 2);
            Indices.Add(first);
            Indices.Add(first + 2);
            Indices.Add(first + 3);
        }
    }
}
