namespace Basin.Render.Gl;

public interface IGlFilter : IFrameFilter
{
    bool Record(in GlFilterContext context);
}
