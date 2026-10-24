using System.Numerics;
using Basin.Scene3D;

namespace Basin.Scene3D.Gltf;

/// <summary>
/// Loads a skinned avatar from a binary glTF file.
/// </summary>
public static class GlbAvatar
{
    /// <summary>
    /// Loads the skeleton, the skinned mesh, the rigid attachments and the named clips of a GLB file. Loading allocates. A frame that uses the result does not.
    /// </summary>
    /// <param name="glb">The bytes of the GLB file.</param>
    /// <param name="sampleHz">The rate in frames per second at which the clips are resampled.</param>
    /// <param name="wanted">The node names of the attachments that start visible. The others load hidden.</param>
    /// <param name="decode">The decoder for the file's first image, or <see langword="null"/> to load untextured.</param>
    /// <returns>The parts of the avatar.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="glb"/> or <paramref name="wanted"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not a GLB file, has no skin, or has more than <see cref="Skeleton.MaxJoints"/> joints.</exception>
    /// <exception cref="NotSupportedException">The file requires an extension, a primitive is not a triangle list, or an animation sampler is not <c>LINEAR</c>, <c>STEP</c> or <c>CUBICSPLINE</c>.</exception>
    public static GlbAvatarModel Load(byte[] glb, int sampleHz, IReadOnlyCollection<string> wanted, GlbImageDecoder? decode = null)
    {
        ArgumentNullException.ThrowIfNull(glb);
        ArgumentNullException.ThrowIfNull(wanted);
        var document = GlbDocument.Parse(glb);
        var nodes = document.Nodes;
        var skins = document.Root.Skins ?? [];
        if (skins.Length == 0 || skins[0].Joints is not { Length: > 0 } joints)
        {
            throw new InvalidDataException("the model has no skin");
        }

        if (joints.Length > Skeleton.MaxJoints)
        {
            throw new InvalidDataException($"the skin has {joints.Length} joints; the pass takes at most {Skeleton.MaxJoints}");
        }

        var parents = document.Parents();
        var names = new string[nodes.Length];
        var translations = new Vector3[nodes.Length];
        var rotations = new Quaternion[nodes.Length];
        var scales = new Vector3[nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            names[i] = node.Name ?? $"node{i}";
            translations[i] = node.Translation is { Length: 3 } t ? new Vector3(t[0], t[1], t[2]) : Vector3.Zero;
            rotations[i] = node.Rotation is { Length: 4 } r ? new Quaternion(r[0], r[1], r[2], r[3]) : Quaternion.Identity;
            scales[i] = node.Scale is { Length: 3 } s ? new Vector3(s[0], s[1], s[2]) : Vector3.One;
            if (node.Matrix is { Length: 16 } && Matrix4x4.Decompose(document.LocalMatrix(i), out var scale, out var rotation, out var translation))
            {
                translations[i] = translation;
                rotations[i] = rotation;
                scales[i] = scale;
            }
        }

        var inverseBinds = skins[0].InverseBindMatrices is { } inverse
            ? document.ReadMatrices(inverse)
            : Enumerable.Repeat(Matrix4x4.Identity, joints.Length).ToArray();
        var skeleton = new Skeleton(names, parents, translations, rotations, scales, joints, inverseBinds);
        var image = decode is not null && (document.Root.Images?.Length ?? 0) > 0 ? decode(document.ReadImage(0)) : null;
        var mesh = SkinnedMeshOf(document, image);
        var attachments = new List<WorldAvatarAttachment>();
        for (var i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].Mesh is not { } meshIndex || nodes[i].Skin is not null)
            {
                continue;
            }

            var vertices = new List<WorldVertex>();
            var indices = new List<uint>();
            GlbMeshes.Append(document, meshIndex, Matrix4x4.Identity, vertices, indices);
            attachments.Add(new WorldAvatarAttachment(names[i], i, new WorldMesh([.. vertices], [.. indices], image))
            {
                Visible = wanted.Contains(names[i]),
            });
        }

        var clips = new Dictionary<string, SkeletonClip>(StringComparer.Ordinal);
        foreach (var animation in document.Root.Animations ?? [])
        {
            if (animation.Name is { } name)
            {
                clips[name] = Resample(document, animation, skeleton, sampleHz);
            }
        }

        return new GlbAvatarModel(skeleton, mesh, attachments, clips);
    }

    private static SkinnedMesh SkinnedMeshOf(GlbDocument document, WorldImage? image)
    {
        var vertices = new List<SkinnedVertex>();
        var indices = new List<uint>();
        var nodes = document.Nodes;
        for (var n = 0; n < nodes.Length; n++)
        {
            if (nodes[n].Mesh is not { } mesh || nodes[n].Skin is null)
            {
                continue;
            }

            foreach (var primitive in (document.Root.Meshes ?? [])[mesh].Primitives ?? [])
            {
                if ((primitive.Mode ?? 4) != 4)
                {
                    throw new NotSupportedException($"primitive mode {primitive.Mode} is not triangles");
                }

                if (primitive.Attributes is not { } attributes ||
                    !attributes.TryGetValue("POSITION", out var positionAccessor) ||
                    !attributes.TryGetValue("JOINTS_0", out var jointAccessor) ||
                    !attributes.TryGetValue("WEIGHTS_0", out var weightAccessor))
                {
                    continue;
                }

                var positions = document.ReadFloats(positionAccessor);
                var normals = attributes.TryGetValue("NORMAL", out var normalAccessor) ? document.ReadFloats(normalAccessor) : null;
                var uvs = attributes.TryGetValue("TEXCOORD_0", out var uvAccessor) ? document.ReadFloats(uvAccessor) : null;
                var jointIndices = document.ReadIntegers(jointAccessor);
                var weights = document.ReadFloats(weightAccessor);
                var first = (uint)vertices.Count;
                var count = positions.Length / 3;
                for (var i = 0; i < count; i++)
                {
                    var weight = new Vector4(weights[i * 4], weights[(i * 4) + 1], weights[(i * 4) + 2], weights[(i * 4) + 3]);
                    var total = weight.X + weight.Y + weight.Z + weight.W;
                    vertices.Add(new SkinnedVertex
                    {
                        Position = new Vector3(positions[i * 3], positions[(i * 3) + 1], positions[(i * 3) + 2]),
                        Normal = normals is null ? Vector3.UnitY : new Vector3(normals[i * 3], normals[(i * 3) + 1], normals[(i * 3) + 2]),
                        Uv = uvs is null ? Vector2.Zero : new Vector2(uvs[i * 2], uvs[(i * 2) + 1]),
                        Joints = new Vector4(jointIndices[i * 4], jointIndices[(i * 4) + 1], jointIndices[(i * 4) + 2], jointIndices[(i * 4) + 3]),
                        Weights = total > 0 ? weight / total : new Vector4(1, 0, 0, 0),
                    });
                }

                if (primitive.Indices is { } indexAccessor)
                {
                    foreach (var index in document.ReadIntegers(indexAccessor))
                    {
                        indices.Add(first + index);
                    }
                }
            }
        }

        return new SkinnedMesh([.. vertices], [.. indices], image);
    }

    private static SkeletonClip Resample(GlbDocument document, GltfAnimation animation, Skeleton skeleton, int hz)
    {
        var channels = animation.Channels ?? [];
        var samplers = animation.Samplers ?? [];
        var animated = new SortedSet<int>();
        var duration = 0f;
        foreach (var channel in channels)
        {
            if (channel.Target?.Node is { } node)
            {
                animated.Add(node);
                var times = document.ReadFloats(samplers[channel.Sampler].Input);
                if (times.Length > 0)
                {
                    duration = MathF.Max(duration, times[^1]);
                }
            }
        }

        var nodes = animated.ToArray();
        var frames = Math.Max(1, (int)MathF.Ceiling(duration * hz) + 1);
        var translations = new Vector3[frames * nodes.Length];
        var rotations = new Quaternion[frames * nodes.Length];
        var scales = new Vector3[frames * nodes.Length];
        for (var k = 0; k < nodes.Length; k++)
        {
            for (var f = 0; f < frames; f++)
            {
                translations[(f * nodes.Length) + k] = skeleton.Translations[nodes[k]];
                rotations[(f * nodes.Length) + k] = skeleton.Rotations[nodes[k]];
                scales[(f * nodes.Length) + k] = skeleton.Scales[nodes[k]];
            }
        }

        foreach (var channel in channels)
        {
            if (channel.Target is not { Node: { } node, Path: { } path })
            {
                continue;
            }

            var k = Array.IndexOf(nodes, node);
            var sampler = samplers[channel.Sampler];
            var times = document.ReadFloats(sampler.Input);
            var values = document.ReadFloats(sampler.Output);
            var mode = sampler.Interpolation ?? "LINEAR";
            if (mode is not ("LINEAR" or "STEP" or "CUBICSPLINE"))
            {
                throw new NotSupportedException($"animation sampler interpolation {mode} is not supported");
            }

            var step = mode == "STEP";
            var cubic = mode == "CUBICSPLINE";
            for (var f = 0; f < frames; f++)
            {
                var time = MathF.Min(f / (float)hz, duration);
                var (a, b, t) = Bracket(times, time);
                if (step)
                {
                    t = 0;
                }

                var at = (f * nodes.Length) + k;
                if (cubic)
                {
                    var span = b > a ? times[b] - times[a] : 0f;
                    switch (path)
                    {
                        case "translation":
                            translations[at] = Hermite(Vec3(values, (3 * a) + 1), Vec3(values, (3 * a) + 2), Vec3(values, (3 * b) + 1), Vec3(values, 3 * b), span, t);
                            break;
                        case "rotation":
                            rotations[at] = Quaternion.Normalize(Hermite(Quat(values, (3 * a) + 1), Quat(values, (3 * a) + 2), Quat(values, (3 * b) + 1), Quat(values, 3 * b), span, t));
                            break;
                        case "scale":
                            scales[at] = Hermite(Vec3(values, (3 * a) + 1), Vec3(values, (3 * a) + 2), Vec3(values, (3 * b) + 1), Vec3(values, 3 * b), span, t);
                            break;
                    }

                    continue;
                }

                switch (path)
                {
                    case "translation":
                        translations[at] = Vector3.Lerp(Vec3(values, a), Vec3(values, b), t);
                        break;
                    case "rotation":
                        rotations[at] = Quaternion.Normalize(Quaternion.Slerp(Quat(values, a), Quat(values, b), t));
                        break;
                    case "scale":
                        scales[at] = Vector3.Lerp(Vec3(values, a), Vec3(values, b), t);
                        break;
                }
            }
        }

        return new SkeletonClip(animation.Name ?? string.Empty, duration, hz, nodes, translations, rotations, scales);
    }

    private static (int A, int B, float T) Bracket(float[] times, float time)
    {
        if (times.Length == 0)
        {
            return (0, 0, 0);
        }

        if (time <= times[0])
        {
            return (0, 0, 0);
        }

        for (var i = 1; i < times.Length; i++)
        {
            if (time <= times[i])
            {
                var span = times[i] - times[i - 1];
                return (i - 1, i, span > 0 ? (time - times[i - 1]) / span : 0);
            }
        }

        return (times.Length - 1, times.Length - 1, 0);
    }

    private static (float Start, float StartTangent, float End, float EndTangent) HermiteWeights(float span, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return ((2 * t3) - (3 * t2) + 1, span * (t3 - (2 * t2) + t), (-2 * t3) + (3 * t2), span * (t3 - t2));
    }

    private static Vector3 Hermite(Vector3 start, Vector3 outTangent, Vector3 end, Vector3 inTangent, float span, float t)
    {
        var (s, so, e, ei) = HermiteWeights(span, t);
        return (start * s) + (outTangent * so) + (end * e) + (inTangent * ei);
    }

    private static Quaternion Hermite(Quaternion start, Quaternion outTangent, Quaternion end, Quaternion inTangent, float span, float t)
    {
        var (s, so, e, ei) = HermiteWeights(span, t);
        return (start * s) + (outTangent * so) + (end * e) + (inTangent * ei);
    }

    private static Vector3 Vec3(float[] values, int index) => new(values[index * 3], values[(index * 3) + 1], values[(index * 3) + 2]);

    private static Quaternion Quat(float[] values, int index) =>
        new(values[index * 4], values[(index * 4) + 1], values[(index * 4) + 2], values[(index * 4) + 3]);
}
