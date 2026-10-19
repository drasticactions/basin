namespace Basin.Capabilities;

public interface IToplevelGeometryFeed
{
    void ReportFrom(ReadOnlySpan<SurfaceBox> boxes);
}
