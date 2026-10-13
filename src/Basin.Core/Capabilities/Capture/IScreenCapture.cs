namespace Basin.Capabilities;

public interface IScreenCapture
{
    bool Supports(in CaptureSource source);

    bool TryDescribe(in CaptureSource source, out CaptureFormat format);

    bool Capture(in CaptureSource source, in Box region, IBuffer target);

    bool CanCaptureYuv => false;

    bool CanCaptureHalfFloat => false;

    string? RendererName => null;

    bool TryColorOf(in CaptureSource source, out CaptureColor color)
    {
        color = default;
        return false;
    }

    bool TryCursorState(IOutput output, out CaptureCursorState cursor);

    void SetCursor(IBuffer? image, in CaptureCursorState state);

    void AddDamageObserver(ICaptureDamageObserver observer);

    void RemoveDamageObserver(ICaptureDamageObserver observer);
}
