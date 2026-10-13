namespace Basin.Session;

public interface ISession : IDisposable
{
    string SeatName { get; }

    bool IsActive { get; }

    event Action? Enabled;

    event Action? Disabled;

    ISessionDevice OpenDevice(string path);

    Task<ISessionDevice> OpenDeviceAsync(string path)
    {
        try
        {
            return Task.FromResult(OpenDevice(path));
        }
        catch (Exception e)
        {
            return Task.FromException<ISessionDevice>(e);
        }
    }

    Task CloseDeviceAsync(ISessionDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.Dispose();
        return Task.CompletedTask;
    }

    void SwitchSession(int session);
}
