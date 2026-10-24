using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// A pose of a <see cref="Skeleton"/> and the arrays it resolves into. It owns the arrays, so a frame does not allocate.
/// </summary>
public sealed class SkeletonPose
{
    private readonly Vector3[] _translations;
    private readonly Quaternion[] _rotations;
    private readonly Vector3[] _scales;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkeletonPose"/> class at the rest pose, resolved.
    /// </summary>
    /// <param name="skeleton">The skeleton to pose.</param>
    public SkeletonPose(Skeleton skeleton)
    {
        Skeleton = skeleton;
        _translations = new Vector3[skeleton.NodeCount];
        _rotations = new Quaternion[skeleton.NodeCount];
        _scales = new Vector3[skeleton.NodeCount];
        World = new Matrix4x4[skeleton.NodeCount];
        Palette = new Matrix4x4[skeleton.Joints.Length];
        Reset();
        Resolve();
    }

    /// <summary>
    /// Gets the skeleton that this pose is for.
    /// </summary>
    public Skeleton Skeleton { get; }

    /// <summary>
    /// Gets the model-space matrix of each node, as of the last <see cref="Resolve"/>.
    /// </summary>
    public Matrix4x4[] World { get; }

    /// <summary>
    /// Gets the skinning matrix of each joint, as of the last <see cref="Resolve"/>. A pass uploads it.
    /// </summary>
    public Matrix4x4[] Palette { get; }

    /// <summary>
    /// Sets every node back to the rest pose. Call <see cref="Resolve"/> after it.
    /// </summary>
    public void Reset()
    {
        Skeleton.Translations.CopyTo(_translations, 0);
        Skeleton.Rotations.CopyTo(_rotations, 0);
        Skeleton.Scales.CopyTo(_scales, 0);
    }

    /// <summary>
    /// Blends a sample of a clip into the pose. Apply clips in order after <see cref="Reset"/>, then call <see cref="Resolve"/>.
    /// </summary>
    /// <param name="clip">The clip to sample. Its node indices must belong to <see cref="Skeleton"/>.</param>
    /// <param name="time">The time in seconds.</param>
    /// <param name="weight">The blend weight, clamped to 1. At 0 or less the call does nothing.</param>
    /// <param name="loop"><see langword="true"/> to wrap the time round the clip, <see langword="false"/> to hold the last frame.</param>
    public void Apply(SkeletonClip clip, float time, float weight, bool loop)
    {
        if (weight <= 0 || clip.FrameCount == 0)
        {
            return;
        }

        weight = MathF.Min(weight, 1);
        var frames = clip.FrameCount;
        var position = time * clip.Rate;
        if (loop && frames > 1)
        {
            var span = frames - 1;
            position %= span;
            if (position < 0)
            {
                position += span;
            }
        }

        position = Math.Clamp(position, 0, frames - 1);
        var first = (int)MathF.Floor(position);
        var second = Math.Min(first + 1, frames - 1);
        var fraction = position - first;
        var nodes = clip.Nodes;
        for (var k = 0; k < nodes.Length; k++)
        {
            var a = (first * nodes.Length) + k;
            var b = (second * nodes.Length) + k;
            var node = nodes[k];
            var translation = Vector3.Lerp(clip.Translations[a], clip.Translations[b], fraction);
            var rotation = Quaternion.Slerp(clip.Rotations[a], clip.Rotations[b], fraction);
            var scale = Vector3.Lerp(clip.Scales[a], clip.Scales[b], fraction);
            if (weight >= 1)
            {
                _translations[node] = translation;
                _rotations[node] = rotation;
                _scales[node] = scale;
            }
            else
            {
                _translations[node] = Vector3.Lerp(_translations[node], translation, weight);
                _rotations[node] = Quaternion.Slerp(_rotations[node], rotation, weight);
                _scales[node] = Vector3.Lerp(_scales[node], scale, weight);
            }
        }
    }

    /// <summary>
    /// Computes <see cref="World"/> and <see cref="Palette"/> from the current pose.
    /// </summary>
    public void Resolve()
    {
        var parents = Skeleton.Parents;
        foreach (var node in Skeleton.Order)
        {
            var local = Matrix4x4.CreateScale(_scales[node]) *
                Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(_rotations[node])) *
                Matrix4x4.CreateTranslation(_translations[node]);
            World[node] = parents[node] >= 0 ? local * World[parents[node]] : local;
        }

        var joints = Skeleton.Joints;
        for (var j = 0; j < joints.Length; j++)
        {
            Palette[j] = Skeleton.InverseBinds[j] * World[joints[j]];
        }
    }
}
