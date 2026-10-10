using System.Net;

namespace Basin.Transport.Waypipe;

public interface IWaypipeListener : IDisposable
{
    EndPoint LocalEndPoint { get; }

    int Live { get; }

    int Accepted { get; }

    event Action<WaypipeChannel>? ChannelAccepted;

    event Action<Exception>? Failed;
}
