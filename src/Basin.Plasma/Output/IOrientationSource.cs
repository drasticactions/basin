namespace Basin.Plasma;

public interface IOrientationSource
{
    bool IsAvailable { get; }

    OutputTransform? Orientation { get; }

    void SetEnabled(bool enabled);

    event Action? Changed;
}
