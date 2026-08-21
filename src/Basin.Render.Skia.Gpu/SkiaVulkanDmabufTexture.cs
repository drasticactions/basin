using Basin.Render.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaVulkanDmabufTexture : ISkiaTexture
{
    private readonly SkiaVulkanRenderer _renderer;
    private readonly VulkanDeviceImage _native;
    private readonly GRBackendTexture _backend;
    private readonly SKImage _image;

    private SkiaVulkanDmabufTexture(SkiaVulkanRenderer renderer, VulkanDeviceImage native, GRBackendTexture backend, SKImage image)
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

    internal static SkiaVulkanDmabufTexture? TryImport(SkiaVulkanRenderer renderer, in DmabufAttributes attributes)
    {
        var native = VulkanDeviceImage.TryImport(
            renderer.Device, attributes,
            Silk.NET.Vulkan.ImageUsageFlags.SampledBit | Silk.NET.Vulkan.ImageUsageFlags.TransferSrcBit | Silk.NET.Vulkan.ImageUsageFlags.TransferDstBit);
        if (native is null)
        {
            return null;
        }

        var backend = SkiaCensus.Track(new GRBackendTexture(native.Width, native.Height, new GRVkImageInfo
        {
            Image = (ulong)native.Image.Handle,
            ImageTiling = SkiaVulkanRenderer.VkImageTilingDrmFormatModifier,
            ImageLayout = SkiaVulkanRenderer.VkImageLayoutGeneral,
            Format = SkiaVulkanRenderer.VkFormatB8G8R8A8Unorm,
            ImageUsageFlags = SkiaVulkanRenderer.VkUsageTexture,
            SampleCount = 1,
            LevelCount = 1,
            CurrentQueueFamily = renderer.Device.QueueFamily,
        }));
        var image = SKImage.FromTexture(
            renderer.Context, backend, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888,
            native.HasAlpha ? SKAlphaType.Premul : SKAlphaType.Opaque);
        if (image is null)
        {
            SkiaCensus.Release(backend);
            native.Dispose();
            return null;
        }

        return new SkiaVulkanDmabufTexture(renderer, native, backend, SkiaCensus.Track(image));
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
