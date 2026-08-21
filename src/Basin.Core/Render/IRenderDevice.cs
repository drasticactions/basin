namespace Basin;

public interface IRenderDevice
{
    int DrmFd { get; }

    string DevicePath { get; }
}
