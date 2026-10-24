namespace Basin;

/// <summary>
/// Describes one candidate for a hardware plane. The consumer fills it, attaches a list of them with <see cref="OutputState.SetLayers"/> and reads <see cref="Accepted"/> after <see cref="IOutput.TestCommit"/>.
/// </summary>
public sealed class OutputLayer
{
    /// <summary>
    /// Gets or sets the buffer to scan out. The layer does not lock it.
    /// </summary>
    public IBuffer? Buffer { get; set; }

    /// <summary>
    /// Gets or sets the part of the buffer to show, in buffer pixels. An empty box means the whole buffer.
    /// </summary>
    public FBox SrcBox { get; set; }

    /// <summary>
    /// Gets or sets the place of the layer on the output, in framebuffer pixels.
    /// </summary>
    public Box DstBox { get; set; }

    /// <summary>
    /// Gets or sets the plane opacity, from 0 to 1.
    /// </summary>
    public float Alpha { get; set; } = 1f;

    /// <summary>
    /// Gets or sets a value indicating whether the layer covers its whole box. An opaque layer lets the backend scan out an alpha format as its opaque variant.
    /// </summary>
    public bool Opaque { get; set; }

    /// <summary>
    /// Gets or sets a sync_file that the plane must wait for before it reads the buffer, or -1 when the content is ready. The fd is borrowed.
    /// </summary>
    public int InFenceFd { get; set; } = -1;

    /// <summary>
    /// Gets or sets a value indicating whether the backend put the layer on a plane. The test commit writes it. The consumer must composite each rejected layer into the primary buffer.
    /// </summary>
    public bool Accepted { get; set; }

    /// <summary>
    /// Gets or sets the YUV matrix of a YUV buffer.
    /// </summary>
    public YuvCoefficients YuvCoefficients { get; set; }

    /// <summary>
    /// Gets or sets the quantization range of a YUV buffer.
    /// </summary>
    public YuvRange YuvRange { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the backend rejected the layer because its plane cannot take the YUV matrix or range. The backend writes it for diagnostics.
    /// </summary>
    public bool YuvEncodingRefused { get; set; }
}
