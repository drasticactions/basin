using Wayland.Server;

namespace Basin;

/// <summary>
/// Holds the clients whose presentation times go on the wire as wall-clock time, for example clients behind a waypipe channel. Every other client gets monotonic times.
/// </summary>
public sealed class WireClockClients
{
    private readonly HashSet<WlClient> _clients = [];

    /// <summary>
    /// Adds a client to the set. The client leaves the set when it is destroyed.
    /// </summary>
    /// <param name="client">The client to add.</param>
    public void Add(WlClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (_clients.Add(client))
        {
            client.Destroyed += () => _clients.Remove(client);
        }
    }

    /// <summary>
    /// Tells whether a client uses wall-clock time on the wire.
    /// </summary>
    /// <param name="client">The client to test.</param>
    /// <returns><see langword="true"/> when the client is in the set.</returns>
    public bool Contains(WlClient client) => _clients.Contains(client);

    /// <summary>
    /// Converts a monotonic time to wall-clock time with the current offset between the two clocks.
    /// </summary>
    /// <param name="monotonicNanos">The time on the monotonic clock in nanoseconds.</param>
    /// <returns>The time in nanoseconds since the Unix epoch.</returns>
    public static ulong ToWire(ulong monotonicNanos) =>
        (ulong)((long)monotonicNanos + (RealtimeClock.Nanos - MonotonicClock.Nanos));

    /// <summary>
    /// Converts a wall-clock time to monotonic time with the current offset between the two clocks.
    /// </summary>
    /// <param name="realtimeNanos">The time in nanoseconds since the Unix epoch.</param>
    /// <returns>The time on the monotonic clock in nanoseconds.</returns>
    public static long FromWire(long realtimeNanos) =>
        realtimeNanos - (RealtimeClock.Nanos - MonotonicClock.Nanos);
}
