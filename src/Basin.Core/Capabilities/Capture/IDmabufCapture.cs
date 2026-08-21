namespace Basin.Capabilities;

public interface IDmabufCapture
{
    bool TryCurrentFrame(IOutput output, out DmabufAttributes attributes);
}
