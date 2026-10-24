namespace Basin.Scene3D.Gltf;

/// <summary>
/// A glTF accessor: a typed view of elements in a buffer view.
/// </summary>
public sealed class GltfAccessor
{
    /// <summary>
    /// Gets or sets the index of the buffer view, or <see langword="null"/> for an accessor with no data. The reader refuses such an accessor.
    /// </summary>
    public int? BufferView { get; set; }

    /// <summary>
    /// Gets or sets the offset in bytes of the first element from the start of the buffer view.
    /// </summary>
    public int ByteOffset { get; set; }

    /// <summary>
    /// Gets or sets the GL component type, for example 5126 for a float or 5123 for an unsigned short.
    /// </summary>
    public int ComponentType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether integer components map to [0, 1] or [-1, 1] when read as floats.
    /// </summary>
    public bool Normalized { get; set; }

    /// <summary>
    /// Gets or sets the number of elements.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the element type: "SCALAR", "VEC2", "VEC3", "VEC4" or "MAT4". The reader refuses other types.
    /// </summary>
    public string Type { get; set; } = "SCALAR";
}
