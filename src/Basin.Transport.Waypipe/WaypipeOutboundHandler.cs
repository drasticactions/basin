using Wayland.Server;

namespace Basin.Transport.Waypipe;

/// <summary>
/// Receives the bytes that the compositor writes to a remote client, with the descriptor slots that travel with them.
/// </summary>
/// <param name="bytes">The Wayland protocol bytes. They are valid only during the call.</param>
/// <param name="fdSlots">The slot numbers of the descriptors that the message carries, in order. They are valid only during the call.</param>
public delegate void WaypipeOutboundHandler(ReadOnlySpan<byte> bytes, ReadOnlySpan<int> fdSlots);
