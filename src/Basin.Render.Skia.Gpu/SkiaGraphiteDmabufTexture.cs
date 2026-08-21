using Basin.Render.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaGraphiteDmabufTexture : ISkiaTexture
{
    private readonly SkiaGraphiteRenderer _renderer;
    private readonly VulkanDeviceImage _native;
    private readonly SKGraphiteBackendTexture _backend;
    private readonly SKImage _image;

    private SkiaGraphiteDmabufTexture(SkiaGraphiteRenderer renderer, VulkanDeviceImage native, SKGraphiteBackendTexture backend, SKImage image)
    {
        _renderer = renderer;
        _native = native;
        _backend = backend;
        _image = image;
        Width = native.Width;
        Height = native.Height;
    }

    public int Width { get; }

    public int Height { get; }

    internal static SkiaGraphiteDmabufTexture? TryImport(SkiaGraphiteRenderer renderer, in DmabufAttributes attributes)
    {
        var native = VulkanDeviceImage.TryImport(
            renderer.Device, attributes,
            Silk.NET.Vulkan.ImageUsageFlags.SampledBit | Silk.NET.Vulkan.ImageUsageFlags.TransferSrcBit | Silk.NET.Vulkan.ImageUsageFlags.TransferDstBit);
        if (native is null)
        {
            return null;
        }

        var backend = SkiaCensus.Track(SKGraphiteBackendTexture.CreateVulkan(
            native.Width, native.Height,
            new SKGraphiteVkTextureInfo
            {
                SampleCount = 1,
                Format = (int)SkiaVulkanRenderer.VkFormatB8G8R8A8Unorm,
                ImageTiling = (int)SkiaVulkanRenderer.VkImageTilingDrmFormatModifier,
                ImageUsageFlags = SkiaVulkanRenderer.VkUsageTexture,
                SharingMode = 0,
                AspectMask = 1,
            },
            (int)SkiaVulkanRenderer.VkImageLayoutGeneral,
            renderer.Device.QueueFamily,
            (nint)native.Image.Handle));
        var image = SKImage.FromTexture(
            renderer.Recorder, backend,
            SKColorType.Bgra8888,
            native.HasAlpha ? SKAlphaType.Premul : SKAlphaType.Opaque);
        if (image is null)
        {
            SkiaCensus.Release(backend);
            native.Dispose();
            return null;
        }

        return new SkiaGraphiteDmabufTexture(renderer, native, backend, SkiaCensus.Track(image));
    }

    public bool Acquire(out SKImage image)
    {
        _renderer.ForeignThisFrame.Add(_native);
        image = _image;
        return true;
    }

    public void Release()
    {
    }

    public void Dispose()
    {
        SkiaCensus.Release(_image);
        SkiaCensus.Release(_backend);
        _native.Dispose();
    }
}
