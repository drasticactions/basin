namespace Basin;

/// <summary>
/// An <see cref="IBuffer"/> over an <see cref="IRemoteImage"/>. It holds one reference to the image until the storage is freed.
/// </summary>
public sealed class RemoteImageBuffer : BufferBase
{
    private readonly IRemoteImage _image;

    /// <summary>Initializes a new instance of the <see cref="RemoteImageBuffer"/> class. It adds one reference to the image.</summary>
    /// <param name="image">The image to wrap.</param>
    public RemoteImageBuffer(IRemoteImage image)
        : base(image.Width, image.Height)
    {
        _image = image;
        image.AddRef();
    }

    /// <summary>Gets the wrapped image.</summary>
    public IRemoteImage Image => _image;

    /// <inheritdoc/>
    public override DrmFormat Format => _image.Format;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    protected override void OnFreeStorage() => _image.Release();
}
