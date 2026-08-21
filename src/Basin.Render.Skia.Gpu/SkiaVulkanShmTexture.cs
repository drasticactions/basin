using Basin.Render.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaVulkanShmTexture : ISkiaTexture, IRefreshableTexture
{
    private readonly SkiaVulkanRenderer _renderer;
    private readonly VulkanUploadImage _native;
    private GRBackendTexture? _backend;
    private SKImage? _image;

    internal SkiaVulkanShmTexture(SkiaVulkanRenderer renderer, IBuffer buffer)
    {
        _renderer = renderer;
        _native = new VulkanUploadImage(renderer.Device, buffer);
        Width = buffer.Width;
        Height = buffer.Height;
    }

    public int Width { get; }

    public int Height { get; }

    public void MarkDirty() => _native.MarkDirty();

    public bool Acquire(out SKImage image)
    {
        if (!_native.PrepareUpload(_renderer.Device.Staging))
        {
            image = null!;
            return false;
        }

        if (_native.NeedsGpuCopy)
        {
            var point = _renderer.Device.SubmitImmediate(_native, static (native, commands) => native.RecordGpuCopy(commands));

            _renderer.Device.Staging.MarkSubmitted(point);
        }

        if (_image is null)
        {
            _backend = SkiaCensus.Track(new GRBackendTexture(Width, Height, new GRVkImageInfo
            {
                Image = (ulong)_native.Image.Handle,
                ImageTiling = SkiaVulkanRenderer.VkImageTilingOptimal,
                ImageLayout = SkiaVulkanRenderer.VkImageLayoutGeneral,
                Format = SkiaVulkanRenderer.VkFormatB8G8R8A8Unorm,
                ImageUsageFlags = SkiaVulkanRenderer.VkUsageTexture,
                SampleCount = 1,
                LevelCount = 1,
                CurrentQueueFamily = _renderer.Device.QueueFamily,
            }));
            var wrapped = SKImage.FromTexture(
                _renderer.Context, _backend, GRSurfaceOrigin.TopLeft, SKColorType.Bgra8888,
                _native.HasAlpha ? SKAlphaType.Premul : SKAlphaType.Opaque);
            if (wrapped is null)
            {
                SkiaCensus.Release(_backend);
                _backend = null;
                image = null!;
                return false;
            }

            _image = SkiaCensus.Track(wrapped);
        }

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
        _image = null;
        _backend = null;
        _native.Dispose();
    }
}
