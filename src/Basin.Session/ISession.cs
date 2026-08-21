namespace Basin.Session;

public interface ISession : IDisposable
{
    string SeatName { get; }

    bool IsActive { get; }

    event Action? Enabled;

    event Action? Disabled;

    ISessionDevice OpenDevice(string path);

    void SwitchSession(int session);
}
