namespace Basin.Scene3D;

/// <summary>
/// A rigid mesh that follows one node of an avatar's skeleton, for example a held item.
/// </summary>
public sealed class WorldAvatarAttachment
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorldAvatarAttachment"/> class.
    /// </summary>
    /// <param name="name">The attachment name.</param>
    /// <param name="node">The index of the node that the mesh follows.</param>
    /// <param name="mesh">The mesh, in the node's space.</param>
    public WorldAvatarAttachment(string name, int node, WorldMesh mesh)
    {
        Name = name;
        Node = node;
        Mesh = mesh;
    }

    /// <summary>
    /// Gets the attachment name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the index of the node that the mesh follows.
    /// </summary>
    public int Node { get; }

    /// <summary>
    /// Gets the mesh, in the node's space.
    /// </summary>
    public WorldMesh Mesh { get; }

    /// <summary>
    /// Gets or sets a value indicating whether a pass draws the attachment.
    /// </summary>
    public bool Visible { get; set; } = true;
}
