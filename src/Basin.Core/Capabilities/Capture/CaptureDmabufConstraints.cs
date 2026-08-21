namespace Basin.Capabilities;

public sealed class CaptureDmabufConstraints : ICaptureDmabufConstraints
{
    private ulong _device;
    private bool _hasDevice;

    public CaptureDmabufConstraints() => Formats = DrmFormatSet.Empty;

    public CaptureDmabufConstraints(DrmFormatSet formats, string devicePath)
        : this() => Offer(formats, devicePath);

    public DrmFormatSet Formats { get; private set; }

    public bool TryDevice(out ulong device)
    {
        device = _device;
        return _hasDevice;
    }

    public void Offer(DrmFormatSet formats, string devicePath)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentException.ThrowIfNullOrEmpty(devicePath);
        Formats = formats;
        _hasDevice = DrmDevices.TryDeviceId(devicePath, out _device);
    }
}
