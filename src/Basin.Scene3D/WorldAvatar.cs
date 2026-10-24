using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// A skinned mesh with rigid attachments and a pose. The consumer animates <see cref="Pose"/> and moves the avatar.
/// </summary>
public sealed class WorldAvatar
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorldAvatar"/> class with a pose at rest.
    /// </summary>
    /// <param name="skeleton">The skeleton that skins the mesh.</param>
    /// <param name="mesh">The skinned mesh.</param>
    /// <param name="attachments">The rigid meshes that follow nodes of the skeleton.</param>
    public WorldAvatar(Skeleton skeleton, SkinnedMesh mesh, IReadOnlyList<WorldAvatarAttachment> attachments)
    {
        Skeleton = skeleton;
        Mesh = mesh;
        Attachments = attachments;
        Pose = new SkeletonPose(skeleton);
    }

    /// <summary>
    /// Gets the skeleton that skins the mesh.
    /// </summary>
    public Skeleton Skeleton { get; }

    /// <summary>
    /// Gets the skinned mesh.
    /// </summary>
    public SkinnedMesh Mesh { get; }

    /// <summary>
    /// Gets the rigid meshes that follow nodes. Iterate with an index, because a <see langword="foreach"/> boxes the enumerator.
    /// </summary>
    public IReadOnlyList<WorldAvatarAttachment> Attachments { get; }

    /// <summary>
    /// Gets the pose that a pass reads the palette and node matrices from.
    /// </summary>
    public SkeletonPose Pose { get; }

    /// <summary>
    /// Gets or sets the position of the avatar's origin in world space.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Gets or sets the turn round the world y axis, in radians.
    /// </summary>
    public float Yaw { get; set; }

    /// <summary>
    /// Gets or sets the uniform scale.
    /// </summary>
    public float Scale { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether a pass draws the avatar.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Gets the model matrix: the scale, then the yaw, then the translation.
    /// </summary>
    public Matrix4x4 Model =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationY(Yaw) * Matrix4x4.CreateTranslation(Position);
}
