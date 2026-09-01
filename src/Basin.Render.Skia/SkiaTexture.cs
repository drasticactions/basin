using Basin.Diagnostics;
using SkiaSharp;
using static Basin.Render.Skia.SkiaLog;

namespace Basin.Render.Skia;

internal sealed class SkiaTexture : ISkiaTexture, IRefreshableTexture
{
    private static readonly HashSet<DrmFormat> Warned = [];

    private readonly IBuffer _buffer;
    private SKImage? _image;
    private nint _imageData;

    internal SkiaTexture(IBuffer buffer)
    {
        _buffer = buffer;
        Width = buffer.Width;
        Height = buffer.Height;
        buffer.Destroyed += DropImage;
    }

    public int Width { get; }

    public int Height { get; }

    public void MarkDirty()
    {
        if (_image is null)
        {
            return;
        }

        AllocationScope.Pause();
        try
        {
            if (_buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
            {
                Rebuild(view);
                _buffer.EndDataAccess();
            }
            else
            {
                DropImage();
            }
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    public bool Acquire(out SKImage image)
    {
        if (!_buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            image = null!;
            return false;
        }

        if (_image is null || _imageData != view.Data)
        {
            Rebuild(view);
        }

        if (_image is null)
        {
            _buffer.EndDataAccess();
            image = null!;
            return false;
        }

        image = _image;
        return true;
    }

    public void Release() => _buffer.EndDataAccess();

    public void Dispose()
    {
        _buffer.Destroyed -= DropImage;
        DropImage();
    }

    private void Rebuild(in BufferDataView view)
    {
        DropImage();
        if (!SkiaRenderer.TryImageInfo(Width, Height, view.Format, out var info))
        {
            if (Warned.Add(view.Format))
            {
                Log.Warn(
                    $"skia: fourcc 0x{(uint)view.Format:x8} has no raster colour type, so surfaces using it stay blank");
            }

            return;
        }

        var image = SKImage.FromPixels(info, view.Data, view.Stride);
        if (image is null)
        {
            return;
        }

        _image = SkiaCensus.Track(image);
        _imageData = view.Data;
    }

    private void DropImage()
    {
        SkiaCensus.Release(_image);
        _image = null;
        _imageData = 0;
    }
}
