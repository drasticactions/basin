using Seatd;

namespace Basin.Session;

/// <summary>
/// An <see cref="ISession"/> without a seat manager. It opens device nodes directly, so the process must have permission on them, and it never becomes inactive.
/// </summary>
public sealed class DirectSession : ISession
{
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    /// <summary>
    /// Gets the seat name. This value is always "seat0".
    /// </summary>
    public string SeatName => "seat0";

    /// <summary>
    /// Gets a value indicating whether the session owns its devices. This value is always <see langword="true"/>.
    /// </summary>
    public bool IsActive => true;

    /// <summary>
    /// This event never occurs. The session cannot be disabled, so the add accessor discards the handler.
    /// </summary>
    public event Action? Enabled
    {
        add { }
        remove { }
    }

    /// <summary>
    /// This event never occurs. The session cannot be disabled, so the add accessor discards the handler.
    /// </summary>
    public event Action? Disabled
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Opens a device node read-write with close-on-exec. The caller owns the result.
    /// </summary>
    /// <param name="path">The absolute path of the device node.</param>
    /// <returns>The open device.</returns>
    /// <exception cref="InvalidOperationException">The open call failed, for example because the process is not in the video or input group.</exception>
    public ISessionDevice OpenDevice(string path)
    {
        var fd = open(path, 0x80002 );
        if (fd < 0)
        {
            throw new InvalidOperationException(
                $"Cannot open {path}, errno {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}. " +
                "Add the user to the video and input groups, or use a seat manager such as seatd or logind.");
        }

        return new DirectDevice(fd, path);
    }

    /// <summary>
    /// Opens a device node on a thread-pool thread.
    /// </summary>
    /// <param name="path">The absolute path of the device node.</param>
    /// <returns>A task that completes with the open device, or faults with <see cref="InvalidOperationException"/>.</returns>
    public Task<ISessionDevice> OpenDeviceAsync(string path) => Task.Run(() => OpenDevice(path));

    /// <summary>
    /// Disposes a device on a thread-pool thread.
    /// </summary>
    /// <param name="device">The device to close.</param>
    /// <returns>A task that completes when the device is closed.</returns>
    public Task CloseDeviceAsync(ISessionDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return Task.Run(device.Dispose);
    }

    /// <summary>
    /// Always raises <see cref="NotSupportedException"/>, because no seat manager is available to switch sessions.
    /// </summary>
    /// <param name="session">The number of the target session. It is not used.</param>
    public void SwitchSession(int session) => throw new NotSupportedException("No seat manager is available to switch sessions.");

    /// <summary>
    /// Does nothing. Each device closes when the caller disposes it.
    /// </summary>
    public void Dispose()
    {
    }

    private sealed class DirectDevice(int fd, string path) : ISessionDevice
    {
        public int FileDescriptor => fd;

        public string Path => path;

        public void Dispose() => close(fd);
    }
}
