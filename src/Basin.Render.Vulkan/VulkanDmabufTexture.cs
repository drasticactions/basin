using Basin.Diagnostics;
using Silk.NET.Vulkan;
using static Basin.Render.Vulkan.VulkanLog;

namespace Basin.Render.Vulkan;

internal sealed unsafe class VulkanDmabufTexture : ITexture, IVulkanRetired
{
    public ulong RetiredAt { get; set; }

    private readonly VulkanRenderer _renderer;
    private readonly ImportedDmabuf _imported;
    private readonly DescriptorAllocation _allocation;
    private readonly ImageView _srgbView;
    private readonly DescriptorAllocation _srgbAllocation;
    private readonly ImageView _ycbcrView;

    internal readonly DmabufAttributes Attributes;

    internal bool OwnedThisPass;

    internal DescriptorSet Set => _allocation.Set;

    internal DescriptorSet LinearSet => _srgbView.Handle != 0 ? _srgbAllocation.Set : _allocation.Set;

    internal readonly bool NeedsShaderDecode;

    internal readonly VulkanRenderer.YcbcrSampler? Ycbcr;

    internal Image Image => _imported.Image;

    private VulkanDmabufTexture(VulkanRenderer renderer, ImportedDmabuf imported, in DmabufAttributes attributes)
    {
        _renderer = renderer;
        _imported = imported;
        Attributes = attributes;
        _ = renderer.Dev.FormatTable.TryGet(attributes.Format, out var props);

        if (props.Entry.IsYcbcr)
        {
            Ycbcr = renderer.GetYcbcrSampler(props.Entry);
            var conversionChain = new SamplerYcbcrConversionInfo
            {
                SType = StructureType.SamplerYcbcrConversionInfo,
                Conversion = Ycbcr.Conversion,
            };
            var ycbcrViewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                PNext = &conversionChain,
                Image = imported.Image,
                ViewType = ImageViewType.Type2D,
                Format = props.Entry.Vk,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            VulkanDevice.Check(renderer.Dev.Api.CreateImageView(renderer.Dev.Device, in ycbcrViewInfo, null, out _ycbcrView), "vkCreateImageView(ycbcr)");
            _allocation = renderer.AllocateYcbcrSet(_ycbcrView, Ycbcr);
            NeedsShaderDecode = true;
            BasinCounters.Track();
            return;
        }

        _allocation = renderer.AllocateTextureSet(imported.View);
        if (imported.UsingMutableSrgb && props.Entry.HasSrgb)
        {
            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = imported.Image,
                ViewType = ImageViewType.Type2D,
                Format = props.Entry.VkSrgb,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            };
            VulkanDevice.Check(renderer.Dev.Api.CreateImageView(renderer.Dev.Device, in viewInfo, null, out _srgbView), "vkCreateImageView(texture srgb)");
            _srgbAllocation = renderer.AllocateTextureSet(_srgbView);
        }

        NeedsShaderDecode = props.Entry.NeedsShaderDecode(_srgbView.Handle != 0);
        BasinCounters.Track();
    }

    internal static VulkanDmabufTexture? TryImport(VulkanRenderer renderer, in DmabufAttributes attributes)
    {
        try
        {
            var imported = VulkanDmabufImport.Import(renderer.Dev, attributes, ImageUsageFlags.SampledBit);
            return new VulkanDmabufTexture(renderer, imported, attributes);
        }
        catch (InvalidOperationException failure)
        {
            Log.Warn(
                $"dmabuf import rejected: {failure.Message} (format=0x{(uint)attributes.Format:X8} modifier=0x{attributes.Modifier:X} planes={attributes.PlaneCount} {attributes.Width}x{attributes.Height})");
            return null;
        }
    }

    public int Width => _imported.Width;

    public int Height => _imported.Height;

    public bool HasAlpha => _imported.HasAlpha;

    public void Dispose()
    {
        BasinCounters.Untrack();
        _renderer.Dev.Ring.Retire(this);
    }

    void IVulkanRetired.ReleaseNow()
    {
        _renderer.Descriptors.Free(_allocation);
        if (_srgbView.Handle != 0)
        {
            _renderer.Descriptors.Free(_srgbAllocation);
            _renderer.Dev.Api.DestroyImageView(_renderer.Dev.Device, _srgbView, null);
        }

        if (_ycbcrView.Handle != 0)
        {
            _renderer.Dev.Api.DestroyImageView(_renderer.Dev.Device, _ycbcrView, null);
        }

        _imported.Destroy(_renderer.Dev);
    }
}
