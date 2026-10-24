namespace Basin;

/// <summary>
/// Sends bytes to a client through a pipe on a managed transport, where no kernel pipe exists.
/// </summary>
public interface IPipeToClient
{
    /// <summary>
    /// Gets a value indicating whether the pipe still accepts bytes.
    /// </summary>
    bool CanWrite { get; }

    /// <summary>
    /// Sends bytes to the client.
    /// </summary>
    /// <param name="bytes">The bytes to send. The implementation copies them.</param>
    void Write(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Closes the write end. The client then reads the end of the stream.
    /// </summary>
    void CloseWrite();
}
