namespace Basin.Capabilities;

/// <summary>
/// Holds the dmabuf formats and the render device that capture destinations can use. It starts with no formats and no device.
/// </summary>
public sealed class CaptureDmabufConstraints : ICaptureDmabufConstraints
{
    private ulong _device;
    private bool _hasDevice;

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptureDmabufConstraints"/> class with no formats and no device.
    /// </summary>
    public CaptureDmabufConstraints() => Formats = DrmFormatSet.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptureDmabufConstraints"/> class and calls <see cref="Offer"/>.
    /// </summary>
    /// <param name="formats">The formats and modifiers that the renderer can draw into.</param>
    /// <param name="devicePath">The path of the render device node.</param>
    public CaptureDmabufConstraints(DrmFormatSet formats, string devicePath)
        : this() => Offer(formats, devicePath);

    /// <summary>
    /// Gets the formats and modifiers that the renderer can draw into. Capture protocols advertise these.
    /// </summary>
    public DrmFormatSet Formats { get; private set; }

    /// <summary>
    /// Gets or sets additional formats and modifiers that <see cref="Wraps"/> accepts as a destination, or <see langword="null"/> for none.
    /// </summary>
    public DrmFormatSet? Wrappable { get; set; }

    /// <summary>
    /// Gets a value that tells whether a client dmabuf can be a capture destination.
    /// </summary>
    /// <param name="format">The pixel format of the buffer.</param>
    /// <param name="modifier">The format modifier of the buffer.</param>
    /// <returns><see langword="true"/> when <see cref="Formats"/> or <see cref="Wrappable"/> contains the pair, otherwise <see langword="false"/>.</returns>
    public bool Wraps(DrmFormat format, ulong modifier) =>
        Formats.Contains(format, modifier) || Wrappable?.Contains(format, modifier) == true;

    /// <summary>
    /// Gets the device number of the render device.
    /// </summary>
    /// <param name="device">The device number, or 0 when it is not known.</param>
    /// <returns><see langword="true"/> when the last device path resolved to a device number, otherwise <see langword="false"/>.</returns>
    public bool TryDevice(out ulong device)
    {
        device = _device;
        return _hasDevice;
    }

    /// <summary>
    /// Occurs after <see cref="Offer"/> replaced the formats and the device.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Replaces the formats and the render device, and raises <see cref="Changed"/>.
    /// </summary>
    /// <param name="formats">The formats and modifiers that the renderer can draw into.</param>
    /// <param name="devicePath">The path of the render device node. It must not be empty.</param>
    public void Offer(DrmFormatSet formats, string devicePath)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentException.ThrowIfNullOrEmpty(devicePath);
        Formats = formats;
        _hasDevice = DrmDevices.TryDeviceId(devicePath, out _device);
        Changed?.Invoke();
    }
}
