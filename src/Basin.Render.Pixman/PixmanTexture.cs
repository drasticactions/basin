using Pixman;

namespace Basin.Render.Pixman;

internal sealed class PixmanTexture : ITexture
{
    private readonly IBuffer _buffer;
    private PixmanImage? _image;
    private nint _imageData;

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

    internal bool AcquireData(out nint data, out int stride, out bool opaque)
    {
        if (!_buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            data = 0;
            stride = 0;
            opaque = false;
            return false;
        }

        data = view.Data;
        stride = view.Stride;
        opaque = view.Format is DrmFormat.Xrgb8888 or DrmFormat.Xbgr8888
            or DrmFormat.Xrgb2101010 or DrmFormat.Xbgr2101010;
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
        if (_image is null || _imageData != view.Data)
        {
            _image?.Dispose();
            _image = PixmanImage.CreateBits(
                PixmanRenderer.ToPixmanFormat(view.Format), Width, Height, view.Data, view.Stride);
            _imageData = view.Data;
        }

        return _image;
    }

    private void DropImage()
    {
        _image?.Dispose();
        _image = null;
        _imageData = 0;
    }
}
