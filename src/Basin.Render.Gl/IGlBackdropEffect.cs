namespace Basin.Render.Gl;

/// <summary>
/// Represents a backdrop effect that the GL renderer accepts. It draws with GL calls on the context of the renderer.
/// </summary>
public interface IGlBackdropEffect : IBackdropEffect
{
    /// <summary>
    /// Draws the effect for one region. The renderer binds its own pass state again after the call.
    /// </summary>
    /// <param name="context">The device, the backdrop texture and the region.</param>
    /// <param name="result">The texture that the renderer composites over the region. It is only read when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="result"/> holds a texture to composite. <see langword="false"/> leaves the backdrop as drawn.</returns>
    bool Record(in GlBackdropContext context, out GlBackdropResult result);
}
