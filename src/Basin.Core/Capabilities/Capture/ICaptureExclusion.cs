namespace Basin.Capabilities;

public interface ICaptureExclusion
{
    bool IsExcludedAt(double x, double y);

    bool IsKeyboardFocusExcluded { get; }
}
