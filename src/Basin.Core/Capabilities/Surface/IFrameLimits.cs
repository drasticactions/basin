namespace Basin.Capabilities;

public interface IFrameLimits
{
    int LimitOf(Surface surface);

    void SetLimit(Surface surface, int fps);

    event Action<Surface>? Changed;
}
