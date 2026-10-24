using Basin.Scene3D;

namespace Basin.Scene3D.Gltf;

/// <summary>
/// The parts of a skinned glTF avatar, ready to build a <see cref="WorldAvatar"/>.
/// </summary>
/// <param name="Skeleton">The skeleton of every node in the file, with the first skin's joints.</param>
/// <param name="Mesh">The skinned meshes of the file, merged into one.</param>
/// <param name="Attachments">One rigid attachment for each node with a mesh and no skin.</param>
/// <param name="Clips">The named animations resampled at a fixed rate, keyed by name.</param>
public sealed record GlbAvatarModel(
    Skeleton Skeleton,
    SkinnedMesh Mesh,
    IReadOnlyList<WorldAvatarAttachment> Attachments,
    IReadOnlyDictionary<string, SkeletonClip> Clips);
