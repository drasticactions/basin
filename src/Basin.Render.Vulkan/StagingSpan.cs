using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

public readonly unsafe struct StagingSpan(Silk.NET.Vulkan.Buffer buffer, ulong offset, void* mapped)
{
    public readonly Silk.NET.Vulkan.Buffer Buffer = buffer;

    public readonly ulong Offset = offset;

    public readonly void* Mapped = mapped;

    public bool IsValid => Mapped is not null;
}
