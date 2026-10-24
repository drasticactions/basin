namespace Basin.Scene3D.Gltf;

/// <summary>
/// A glTF image. The reader reads only an image that is stored in a buffer view.
/// </summary>
public sealed class GltfImage
{
    /// <summary>
    /// Gets or sets the index of the buffer view that holds the encoded image, or <see langword="null"/> for an image at a URI.
    /// </summary>
    public int? BufferView { get; set; }

    /// <summary>
    /// Gets or sets the media type of the encoded image, for example "image/png".
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the image name.
    /// </summary>
    public string? Name { get; set; }
}
