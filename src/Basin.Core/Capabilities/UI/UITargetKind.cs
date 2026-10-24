namespace Basin.Capabilities;

/// <summary>
/// The buffer kinds that a UI host can produce. <see cref="IUIHost.Produces"/> combines them, and a surface request names one.
/// </summary>
[Flags]
public enum UITargetKind
{
    /// <summary>Buffers in CPU memory.</summary>
    Memory = 1,

    /// <summary>dmabuf buffers that a GPU renderer imports.</summary>
    Dmabuf = 2,

    /// <summary>GL textures.</summary>
    GlTexture = 4,
}
