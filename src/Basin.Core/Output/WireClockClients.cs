using Wayland.Server;

namespace Basin;

public sealed class WireClockClients
{
    private readonly HashSet<WlClient> _clients = [];

    public void Add(WlClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (_clients.Add(client))
        {
            client.Destroyed += () => _clients.Remove(client);
        }
    }

    public bool Contains(WlClient client) => _clients.Contains(client);

    public static ulong ToWire(ulong monotonicNanos) =>
        (ulong)((long)monotonicNanos + (RealtimeClock.Nanos - MonotonicClock.Nanos));

    public static long FromWire(long realtimeNanos) =>
        realtimeNanos - (RealtimeClock.Nanos - MonotonicClock.Nanos);
}
