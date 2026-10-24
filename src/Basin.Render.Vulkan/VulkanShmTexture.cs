using Basin.Diagnostics;
using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal sealed class VulkanShmTexture : ITexture, IRefreshableTexture, IVulkanRetired
{
    public ulong RetiredAt { get; set; }

    private readonly VulkanRenderer _renderer;
    private readonly VulkanUploadImage _upload;
    private readonly DescriptorAllocation _allocation;
    private readonly DescriptorAllocation _srgbAllocation;
    private readonly bool _hasSrgbSet;

    internal DescriptorSet Set => _allocation.Set;

    internal DescriptorSet LinearSet => _hasSrgbSet ? _srgbAllocation.Set : _allocation.Set;

    internal bool NeedsShaderDecode => _upload.NeedsShaderDecode;

    internal Image Image => _upload.Image;

    internal Format VkFormat => _upload.VkFormat;

    internal ImageView View => _upload.View;

    internal VulkanDevice Device => _renderer.Dev;

    internal VulkanShmTexture(VulkanRenderer renderer, IBuffer buffer)
    {
        _renderer = renderer;
        _upload = new VulkanUploadImage(renderer.Dev, buffer);
        var allocated = false;
        try
        {
            _allocation = renderer.AllocateTextureSet(_upload.View);
            allocated = true;
            if (_upload.SrgbView.Handle != 0)
            {
                _srgbAllocation = renderer.AllocateTextureSet(_upload.SrgbView);
                _hasSrgbSet = true;
            }
        }
        catch
        {
            if (allocated)
            {
                renderer.Descriptors.Free(_allocation);
            }

            _upload.Dispose();
            throw;
        }

        BasinCounters.Track();
    }

    public int Width => _upload.Width;

    public int Height => _upload.Height;

    public bool HasAlpha => _upload.HasAlpha;

    public void MarkDirty() => _upload.MarkDirty();

    public void MarkDirty(in Box damage) => _upload.MarkDirty(damage);

    public bool TryAdopt(IBuffer source, in Box damage) => _upload.TryAdopt(source, damage);

    internal bool PrepareUpload(VulkanStagingPool staging) => _upload.PrepareUpload(staging);

    internal bool NeedsGpuCopy => _upload.NeedsGpuCopy;

    internal void RecordGpuCopy(Silk.NET.Vulkan.CommandBuffer commands) => _upload.RecordGpuCopy(commands);

    public void Dispose()
    {
        BasinCounters.Untrack();
        _renderer.Dev.Ring.Retire(this);
    }

    void IVulkanRetired.ReleaseNow()
    {
        _renderer.Descriptors.Free(_allocation);
        if (_hasSrgbSet)
        {
            _renderer.Descriptors.Free(_srgbAllocation);
        }

        _upload.Dispose();
    }
}
