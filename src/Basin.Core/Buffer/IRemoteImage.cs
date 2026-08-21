namespace Basin;

public interface IRemoteImage
{
    int Width { get; }

    int Height { get; }

    DrmFormat Format { get; }

    int Stride { get; }

    nint Pixels { get; }

    bool IsReleased { get; }

    void AddRef();

    void Release();
}
