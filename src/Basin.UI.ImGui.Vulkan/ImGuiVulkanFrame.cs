using Basin.Render.Vulkan;
using Silk.NET.Vulkan;

namespace Basin.UI.ImGui;

internal sealed class ImGuiVulkanFrame : IDisposable
{
    private readonly VulkanDevice _device;
    private ImGuiVulkanBuffer? _vertices;
    private ImGuiVulkanBuffer? _indices;

    internal ImGuiVulkanFrame(VulkanDevice device)
    {
        _device = device;
    }

    internal ulong Point { get; set; }

    internal bool IsIdle => Point == 0 || _device.IsComplete(Point);

    internal ImGuiVulkanBuffer Vertices(int bytes) => Reserve(ref _vertices, bytes, BufferUsageFlags.VertexBufferBit);

    internal ImGuiVulkanBuffer Indices(int bytes) => Reserve(ref _indices, bytes, BufferUsageFlags.IndexBufferBit);

    public void Dispose()
    {
        _vertices?.Destroy(_device);
        _indices?.Destroy(_device);
        _vertices = null;
        _indices = null;
    }

    private ImGuiVulkanBuffer Reserve(ref ImGuiVulkanBuffer? buffer, int bytes, BufferUsageFlags usage)
    {
        if (buffer is null || buffer.Size < (ulong)bytes)
        {
            var capacity = Math.Max(Math.Max((ulong)bytes, 64UL * 1024), buffer is null ? 0 : buffer.Size * 2);
            buffer?.Destroy(_device);
            buffer = ImGuiVulkanBuffer.Create(_device, capacity, usage);
        }

        return buffer;
    }
}
