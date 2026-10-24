namespace Basin;

/// <summary>
/// Pairs a renderer with the allocator for buffers on its device.
/// </summary>
/// <param name="Renderer">The renderer.</param>
/// <param name="DeviceAllocator">The allocator for render targets on the renderer's device, or <see langword="null"/> for a CPU renderer.</param>
public sealed record RenderStack(IRenderer Renderer, IAllocator? DeviceAllocator)
{
    /// <summary>
    /// Gets a value indicating whether render targets must be CPU-mappable memory, which is when there is no device allocator.
    /// </summary>
    public bool NeedsMappableTarget => DeviceAllocator is null;
}
