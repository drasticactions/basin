using Wayland.Server;

namespace Basin.Transport.Waypipe;

public delegate void WaypipeOutboundHandler(ReadOnlySpan<byte> bytes, ReadOnlySpan<int> fdSlots);
