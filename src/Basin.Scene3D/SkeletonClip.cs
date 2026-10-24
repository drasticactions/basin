using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// An animation resampled at a fixed rate. Each frame holds one pose for each animated node.
/// </summary>
public sealed class SkeletonClip
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkeletonClip"/> class. It keeps the arrays and does not copy them.
    /// </summary>
    /// <param name="name">The clip name.</param>
    /// <param name="duration">The length in seconds.</param>
    /// <param name="rate">The sample rate in frames per second. It must be positive.</param>
    /// <param name="nodes">The node indices that the clip animates.</param>
    /// <param name="translations">The translations, frame by frame, one for each animated node.</param>
    /// <param name="rotations">The rotations, in the same order as <paramref name="translations"/>.</param>
    /// <param name="scales">The scales, in the same order as <paramref name="translations"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is not positive.</exception>
    /// <exception cref="ArgumentException">The pose arrays do not hold a whole number of frames, or they differ in length.</exception>
    public SkeletonClip(
        string name, float duration, float rate, int[] nodes, Vector3[] translations, Quaternion[] rotations, Vector3[] scales)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(rate, 0);
        var frames = nodes.Length == 0 ? 0 : translations.Length / nodes.Length;
        if (translations.Length != frames * nodes.Length || rotations.Length != translations.Length || scales.Length != translations.Length)
        {
            throw new ArgumentException("Every frame must hold one pose per animated node.", nameof(translations));
        }

        Name = name;
        Duration = duration;
        Rate = rate;
        Nodes = nodes;
        Translations = translations;
        Rotations = rotations;
        Scales = scales;
        FrameCount = frames;
    }

    /// <summary>
    /// Gets the clip name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the length in seconds.
    /// </summary>
    public float Duration { get; }

    /// <summary>
    /// Gets the sample rate in frames per second.
    /// </summary>
    public float Rate { get; }

    /// <summary>
    /// Gets the node indices that the clip animates.
    /// </summary>
    public int[] Nodes { get; }

    /// <summary>
    /// Gets the number of frames.
    /// </summary>
    public int FrameCount { get; }

    /// <summary>
    /// Gets the translations. Frame f of node k is at index f times the node count plus k.
    /// </summary>
    public Vector3[] Translations { get; }

    /// <summary>
    /// Gets the rotations, in the same order as <see cref="Translations"/>.
    /// </summary>
    public Quaternion[] Rotations { get; }

    /// <summary>
    /// Gets the scales, in the same order as <see cref="Translations"/>.
    /// </summary>
    public Vector3[] Scales { get; }
}
