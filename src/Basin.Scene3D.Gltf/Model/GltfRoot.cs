namespace Basin.Scene3D.Gltf;

/// <summary>
/// The JSON chunk of a glTF file, limited to the parts that the reader uses.
/// </summary>
public sealed class GltfRoot
{
    /// <summary>
    /// Gets or sets the scenes.
    /// </summary>
    public GltfScene[]? Scenes { get; set; }

    /// <summary>
    /// Gets or sets the index of the default scene, or <see langword="null"/> to use scene 0.
    /// </summary>
    public int? Scene { get; set; }

    /// <summary>
    /// Gets or sets the nodes.
    /// </summary>
    public GltfNode[]? Nodes { get; set; }

    /// <summary>
    /// Gets or sets the meshes.
    /// </summary>
    public GltfMesh[]? Meshes { get; set; }

    /// <summary>
    /// Gets or sets the accessors.
    /// </summary>
    public GltfAccessor[]? Accessors { get; set; }

    /// <summary>
    /// Gets or sets the buffer views.
    /// </summary>
    public GltfBufferView[]? BufferViews { get; set; }

    /// <summary>
    /// Gets or sets the skins. The avatar loader uses only the first one.
    /// </summary>
    public GltfSkin[]? Skins { get; set; }

    /// <summary>
    /// Gets or sets the animations.
    /// </summary>
    public GltfAnimation[]? Animations { get; set; }

    /// <summary>
    /// Gets or sets the images.
    /// </summary>
    public GltfImage[]? Images { get; set; }

    /// <summary>
    /// Gets or sets the extensions that a reader must support. <see cref="GlbDocument.Parse"/> refuses a file that names any.
    /// </summary>
    public string[]? ExtensionsRequired { get; set; }
}
