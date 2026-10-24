namespace Basin;

/// <summary>
/// A reference-counted CPU image that a remote transport delivers in place of a dmabuf fd.
/// </summary>
public interface IRemoteImage
{
    /// <summary>Gets the width in pixels.</summary>
    int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    int Height { get; }

    /// <summary>Gets the pixel format.</summary>
    DrmFormat Format { get; }

    /// <summary>Gets the row stride in bytes.</summary>
    int Stride { get; }

    /// <summary>Gets the address of the first row. It is not valid after <see cref="IsReleased"/> becomes <see langword="true"/>.</summary>
    nint Pixels { get; }

    /// <summary>Gets a value indicating whether the last reference is gone and the pixels are freed.</summary>
    bool IsReleased { get; }

    /// <summary>Adds one reference to the image.</summary>
    void AddRef();

    /// <summary>Removes one reference. The image frees its pixels when the count reaches zero.</summary>
    void Release();
}
