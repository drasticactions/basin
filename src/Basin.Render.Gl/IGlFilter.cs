namespace Basin.Render.Gl;

/// <summary>
/// Represents a frame filter that the GL renderer accepts. It draws with GL calls on the context of the renderer.
/// </summary>
public interface IGlFilter : IFrameFilter
{
    /// <summary>
    /// Draws the filter, which reads the source texture and writes the whole target. The renderer binds its own pass state again after the call.
    /// </summary>
    /// <param name="context">The device, the source and target textures and the per-frame options.</param>
    /// <returns><see langword="true"/> when the filter wrote the target. On <see langword="false"/> the caller draws the source itself.</returns>
    bool Record(in GlFilterContext context);
}
