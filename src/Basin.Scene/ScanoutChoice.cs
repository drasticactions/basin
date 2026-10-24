namespace Basin.Scene;

/// <summary>
/// Names the buffer allocation that an output driver chose for an output's swapchain.
/// </summary>
public enum ScanoutChoice
{
    /// <summary>
    /// The swapchain uses render-device buffers with modifiers that the display or the parent compositor can scan out.
    /// </summary>
    DeviceBuffers,

    /// <summary>
    /// The swapchain uses linear dumb buffers. Every frame is rendered in software or read back from the GPU.
    /// </summary>
    DumbLinear,

    /// <summary>
    /// The primary plane refused the device buffers on the first commit, so the driver falls back to dumb buffers.
    /// </summary>
    RefusedByPlane,
}
