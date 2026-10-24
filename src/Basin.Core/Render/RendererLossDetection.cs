namespace Basin;

/// <summary>
/// Tells how a renderer finds out that it lost its GPU device or context.
/// </summary>
public enum RendererLossDetection
{
    /// <summary>
    /// The renderer cannot lose its device, for example a CPU renderer. It never raises <see cref="IRenderer.DeviceLost"/>.
    /// </summary>
    Never,

    /// <summary>
    /// The context has reset notification, and the renderer reads the reset status at the start of each pass.
    /// </summary>
    ResetNotification,

    /// <summary>
    /// The context has no reset notification. The renderer reads a loss from context-lost errors only, so detection is best effort.
    /// </summary>
    Errors,

    /// <summary>
    /// The device or the context reports its lost state, for example a Vulkan result or an abandoned Skia context.
    /// </summary>
    DeviceStatus,
}
