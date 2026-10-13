using System.Runtime.InteropServices;
using Pixman;

namespace Basin.Render.Pixman;

internal sealed class PixmanTexture : ITexture
{
    private readonly IBuffer _buffer;
    private PixmanImage? _image;
    private nint _imageData;
    private nint _repack;
    private int _repackStride;
    private nint _shadow;
    private PixmanImage? _shadowImage;

    internal PixmanTexture(IBuffer buffer)
    {
        _buffer = buffer;
        Width = buffer.Width;
        Height = buffer.Height;
        buffer.Destroyed += DropImage;
        if (buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            EnsureImage(in view);
            buffer.EndDataAccess();
        }
    }

    public int Width { get; }

    public int Height { get; }

    internal unsafe bool AcquireData(out nint data, out int stride, out bool opaque)
    {
        if (!_buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            data = 0;
            stride = 0;
            opaque = false;
            return false;
        }

        opaque = view.Format.IsOpaque();
        if (view.Format is DrmFormat.Argb8888 or DrmFormat.Xrgb8888)
        {
            data = view.Data;
            stride = view.Stride;
            return true;
        }

        var image = EnsureImage(in view);
        if (_shadowImage is null)
        {
            _shadow = (nint)NativeMemory.Alloc((nuint)(Width * Height * 4));
            _shadowImage = PixmanImage.CreateBits(PixmanFormat.A8R8G8B8, Width, Height, _shadow, Width * 4);
        }

        _shadowImage.Composite(PixmanOp.Src, image, null, 0, 0, 0, 0, 0, 0, Width, Height);
        data = _shadow;
        stride = Width * 4;
        return true;
    }

    internal bool Acquire(out PixmanImage image)
    {
        if (!_buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            image = null!;
            return false;
        }

        image = EnsureImage(in view);
        return true;
    }

    internal void Release() => _buffer.EndDataAccess();

    public void Dispose()
    {
        _buffer.Destroyed -= DropImage;
        DropImage();
    }

    private PixmanImage EnsureImage(in BufferDataView view)
    {
        var data = view.Data;
        var stride = view.Stride;
        if (stride % 4 != 0)
        {
            data = Repack(in view);
            stride = _repackStride;
        }

        if (_image is null || _imageData != view.Data)
        {
            _image?.Dispose();
            _image = PixmanImage.CreateBits(PixmanRenderer.ToPixmanFormat(view.Format), Width, Height, data, stride);
            _imageData = view.Data;
        }

        return _image;
    }

    private unsafe nint Repack(in BufferDataView view)
    {
        if (_repack == 0)
        {
            _repackStride = (view.Stride + 3) & ~3;
            _repack = (nint)NativeMemory.Alloc((nuint)(_repackStride * Height));
        }

        var row = Width * view.Format.BytesPerPixel();
        for (var y = 0; y < Height; y++)
        {
            Buffer.MemoryCopy(
                (void*)(view.Data + (y * view.Stride)), (void*)(_repack + (y * _repackStride)), _repackStride, row);
        }

        return _repack;
    }

    private unsafe void DropImage()
    {
        _image?.Dispose();
        _image = null;
        _imageData = 0;
        _shadowImage?.Dispose();
        _shadowImage = null;
        if (_shadow != 0)
        {
            NativeMemory.Free((void*)_shadow);
            _shadow = 0;
        }

        if (_repack != 0)
        {
            NativeMemory.Free((void*)_repack);
            _repack = 0;
        }
    }
}
