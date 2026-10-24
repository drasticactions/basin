namespace Basin.Scene3D;

/// <summary>
/// A 3D pass that draws one output's slice of a <see cref="World"/> into a buffer.
/// The result is an ordinary buffer that the consumer puts in the 2D scene.
/// </summary>
public interface IWorldRenderer : IDisposable
{
    /// <summary>
    /// Gets the short name of the renderer row, for example "gl" or "vulkan".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Draws the world, the view and the visible set in <paramref name="frame"/> into <paramref name="target"/>.
    /// The pass fences the target and the sampled client buffers before it returns.
    /// </summary>
    /// <param name="frame">The world, the view, the visible set and the settings for this output.</param>
    /// <param name="target">The buffer to draw into. The renderer caches its state for it until the buffer is destroyed.</param>
    /// <returns><see langword="true"/> when the frame was drawn.</returns>
    bool Render(in WorldFrame frame, IBuffer target);

    /// <summary>
    /// Releases the mip copies and other per-window state that the renderer keeps for <paramref name="window"/>.
    /// Call it when the window leaves the world.
    /// </summary>
    /// <param name="window">The window that is gone.</param>
    void Forget(WorldWindow window);
}
