namespace Basin.Capabilities;

public interface ICaptureDmabufConstraints
{
    bool TryDevice(out ulong device);

    DrmFormatSet Formats { get; }

    bool Wraps(DrmFormat format, ulong modifier) => Formats.Contains(format, modifier);
}
