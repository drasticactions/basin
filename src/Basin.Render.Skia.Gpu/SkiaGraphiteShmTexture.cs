using Basin.Render.Vulkan;
using SkiaSharp;

namespace Basin.Render.Skia;

internal sealed class SkiaGraphiteShmTexture : ISkiaTexture, IRefreshableTexture
{
    private readonly SkiaGraphiteRenderer _renderer;
    private readonly VulkanUploadImage _native;
    private SKGraphiteBackendTexture? _backend;
    private SKImage? _image;

    internal SkiaGraphiteShmTexture(SkiaGraphiteRenderer renderer, IBuffer buffer)
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
            _backend = SkiaCensus.Track(SKGraphiteBackendTexture.CreateVulkan(
                Width, Height,
                new SKGraphiteVkTextureInfo
                {
                    SampleCount = 1,
                    Format = (int)SkiaVulkanRenderer.VkFormatB8G8R8A8Unorm,
                    ImageTiling = (int)SkiaVulkanRenderer.VkImageTilingOptimal,
                    ImageUsageFlags = SkiaVulkanRenderer.VkUsageTexture,
                    SharingMode = 0,
                    AspectMask = 1,
                },
                (int)SkiaVulkanRenderer.VkImageLayoutGeneral,
                _renderer.Device.QueueFamily,
                (nint)_native.Image.Handle));
            var wrapped = SKImage.FromTexture(
                _renderer.Recorder, _backend,
                SKColorType.Bgra8888,
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
