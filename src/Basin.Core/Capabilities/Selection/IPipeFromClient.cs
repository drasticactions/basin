namespace Basin;

/// <summary>
/// Receives the bytes that a client writes into a pipe on a managed transport, where no kernel pipe exists.
/// </summary>
public interface IPipeFromClient
{
    /// <summary>
    /// Receives the next bytes that the client wrote.
    /// </summary>
    /// <param name="bytes">The bytes. They are valid only during the call.</param>
    void Deliver(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Tells the receiver that the client closed its end. No more bytes follow.
    /// </summary>
    void Complete();
}
