namespace Basin.Session;

/// <summary>
/// A seat session that grants access to DRM and input devices. A device file descriptor can become invalid when the session is disabled.
/// </summary>
public interface ISession : IDisposable
{
    /// <summary>
    /// Gets the name of the seat that the session controls, for example "seat0".
    /// </summary>
    string SeatName { get; }

    /// <summary>
    /// Gets a value indicating whether the session currently owns its devices, for example while its virtual terminal is in the foreground.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Occurs when the session becomes active again. A handler must treat the state of every open device as stale.
    /// </summary>
    event Action? Enabled;

    /// <summary>
    /// Occurs when the session loses its devices. A handler must stop all device access before it returns.
    /// </summary>
    event Action? Disabled;

    /// <summary>
    /// Opens a device node through the session. The caller owns the result and disposes it to close the device.
    /// </summary>
    /// <param name="path">The absolute path of the device node, for example "/dev/dri/card0".</param>
    /// <returns>The open device and its file descriptor.</returns>
    ISessionDevice OpenDevice(string path);

    /// <summary>
    /// Opens a device node without a block on the caller where the implementation can do so. The default implementation calls <see cref="OpenDevice(string)"/> synchronously.
    /// </summary>
    /// <param name="path">The absolute path of the device node.</param>
    /// <returns>A task that completes with the open device, or faults with the error that <see cref="OpenDevice(string)"/> raised.</returns>
    Task<ISessionDevice> OpenDeviceAsync(string path)
    {
        try
        {
            return Task.FromResult(OpenDevice(path));
        }
        catch (Exception e)
        {
            return Task.FromException<ISessionDevice>(e);
        }
    }

    /// <summary>
    /// Closes a device that this session opened. The default implementation disposes <paramref name="device"/> synchronously.
    /// </summary>
    /// <param name="device">The device to close.</param>
    /// <returns>A task that completes when the device is closed.</returns>
    Task CloseDeviceAsync(ISessionDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asks the seat manager to switch to another session, for example another virtual terminal.
    /// </summary>
    /// <param name="session">The number of the target session.</param>
    void SwitchSession(int session);
}
