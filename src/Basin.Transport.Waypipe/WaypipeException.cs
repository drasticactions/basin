using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

public sealed class WaypipeException : Exception
{
    public WaypipeException(string message)
        : base(message)
    {
    }

    public WaypipeException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
