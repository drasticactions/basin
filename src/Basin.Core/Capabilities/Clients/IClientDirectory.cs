namespace Basin.Capabilities;

/// <summary>
/// Lists the connected Wayland clients and, where the transport allows it, the objects each one holds.
/// Callers must use it on the compositor thread.
/// </summary>
public interface IClientDirectory
{
    /// <summary>
    /// Gets a value indicating whether <see cref="Objects"/> can walk a client's objects. It is false when the
    /// transport is not libwayland.
    /// </summary>
    bool CanListObjects { get; }

    /// <summary>
    /// Copies the connected clients into a caller buffer.
    /// </summary>
    /// <param name="clients">The buffer to fill.</param>
    /// <returns>The number of clients written, or -1 when the buffer is too small. Nothing useful is written then.</returns>
    int Enumerate(Span<ClientInfo> clients);

    /// <summary>
    /// Counts a client's live objects by interface and version.
    /// </summary>
    /// <param name="clientId">The <see cref="ClientInfo.Id"/> of the client.</param>
    /// <param name="objects">The buffer to fill.</param>
    /// <returns>The number of entries written, 0 when the client is gone or objects cannot be listed, or -1 when the buffer is too small.</returns>
    int Objects(ulong clientId, Span<ClientObjectInfo> objects);
}
