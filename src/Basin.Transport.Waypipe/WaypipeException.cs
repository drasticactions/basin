using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

/// <summary>
/// The error for a waypipe stream that breaks the protocol or the channel limits. A channel ends when it gets one.
/// </summary>
public sealed class WaypipeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WaypipeException"/> class.
    /// </summary>
    /// <param name="message">The description of the protocol error.</param>
    public WaypipeException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WaypipeException"/> class with the error that caused it.
    /// </summary>
    /// <param name="message">The description of the protocol error.</param>
    /// <param name="inner">The error that caused this error, for example a decompression failure.</param>
    public WaypipeException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
