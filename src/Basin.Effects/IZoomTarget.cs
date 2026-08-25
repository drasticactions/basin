namespace Basin.Effects;

public interface IZoomTarget
{
    bool TryGetFocus(out Box rectangle, out long reportedAtNanos);
}
