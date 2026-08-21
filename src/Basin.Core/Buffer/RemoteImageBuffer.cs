namespace Basin;

public sealed class RemoteImageBuffer : BufferBase
{
    private readonly IRemoteImage _image;

    public RemoteImageBuffer(IRemoteImage image)
        : base(image.Width, image.Height)
    {
        _image = image;
        image.AddRef();
    }

    public IRemoteImage Image => _image;

    protected override bool TryMap(BufferDataAccess access, out BufferDataView view)
    {
        if (_image.IsReleased)
        {
            view = default;
            return false;
        }

        view = new BufferDataView(_image.Pixels, _image.Stride, _image.Format);
        return true;
    }

    protected override void OnFreeStorage() => _image.Release();
}
