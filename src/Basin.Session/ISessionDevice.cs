namespace Basin.Session;

/// <summary>
/// A device node that an <see cref="ISession"/> opened. Disposal closes the device through the session.
/// </summary>
public interface ISessionDevice : IDisposable
{
    /// <summary>
    /// Gets the file descriptor of the device. It stays valid until disposal, but the session can revoke access while it is disabled.
    /// </summary>
    int FileDescriptor { get; }

    /// <summary>
    /// Gets the device path that the caller passed to <see cref="ISession.OpenDevice(string)"/>.
    /// </summary>
    string Path { get; }
}
