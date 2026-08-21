namespace Basin;

public interface ICrossDeviceConversion : IDisposable
{
    IBuffer Buffer { get; }

    void Refresh();
}
