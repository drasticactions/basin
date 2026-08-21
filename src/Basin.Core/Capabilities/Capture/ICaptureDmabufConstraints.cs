namespace Basin.Capabilities;

public interface ICaptureDmabufConstraints
{
    bool TryDevice(out ulong device);

    DrmFormatSet Formats { get; }
}
