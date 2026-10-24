using Basin.Capabilities;

namespace Basin;

/// <summary>
/// Represents a backdrop effect that reads a frame shape per key, for example glass.
/// </summary>
public interface IShapedBackdropEffect : IBackdropEffect
{
    /// <summary>
    /// Sets the shape that the effect uses for the draws with a key. It replaces an earlier shape for the same key.
    /// </summary>
    /// <param name="key">The key that the draws use.</param>
    /// <param name="shape">The shape of the frame.</param>
    void SetShape(object key, in FrameBackdropShape shape);
}
