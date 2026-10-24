using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

/// <summary>
/// Represents a host-visible, coherent range of a staging buffer that <see cref="VulkanStagingPool.Allocate"/> returned. The default value is not valid.
/// </summary>
/// <param name="buffer">The Vulkan buffer that holds the range.</param>
/// <param name="offset">The start of the range in the buffer, in bytes.</param>
/// <param name="mapped">The CPU address of the start of the range.</param>
public readonly unsafe struct StagingSpan(Silk.NET.Vulkan.Buffer buffer, ulong offset, void* mapped)
{
    /// <summary>
    /// The Vulkan buffer that holds the range. The pool owns it.
    /// </summary>
    public readonly Silk.NET.Vulkan.Buffer Buffer = buffer;

    /// <summary>
    /// The start of the range in <see cref="Buffer"/>, in bytes.
    /// </summary>
    public readonly ulong Offset = offset;

    /// <summary>
    /// The CPU address of the start of the range. It stays valid until the pool recycles the buffer.
    /// </summary>
    public readonly void* Mapped = mapped;

    /// <summary>
    /// Gets a value indicating whether the pool supplied the range. It is <see langword="false"/> when the allocation failed.
    /// </summary>
    public bool IsValid => Mapped is not null;
}
