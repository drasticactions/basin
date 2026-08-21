namespace Basin.Capabilities;

public interface IScreenCapture
{
    bool Supports(in CaptureSource source);

    bool TryDescribe(in CaptureSource source, out CaptureFormat format);

    bool Capture(in CaptureSource source, in Box region, IBuffer target);

    bool TryCursorState(IOutput output, out CaptureCursorState cursor);

    void AddDamageObserver(ICaptureDamageObserver observer);

    void RemoveDamageObserver(ICaptureDamageObserver observer);
}
