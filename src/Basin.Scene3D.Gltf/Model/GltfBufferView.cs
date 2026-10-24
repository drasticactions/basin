namespace Basin.Scene3D.Gltf;

/// <summary>
/// A glTF buffer view: a byte range of a buffer.
/// </summary>
public sealed class GltfBufferView
{
    /// <summary>
    /// Gets or sets the index of the buffer. The reader reads only buffer 0, the GLB binary chunk.
    /// </summary>
    public int Buffer { get; set; }

    /// <summary>
    /// Gets or sets the offset in bytes from the start of the buffer.
    /// </summary>
    public int ByteOffset { get; set; }

    /// <summary>
    /// Gets or sets the length in bytes.
    /// </summary>
    public int ByteLength { get; set; }

    /// <summary>
    /// Gets or sets the distance in bytes between elements, or <see langword="null"/> for tightly packed elements.
    /// </summary>
    public int? ByteStride { get; set; }
}
