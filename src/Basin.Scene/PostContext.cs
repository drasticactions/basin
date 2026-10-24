using Pixman;

namespace Basin.Scene;

/// <summary>
/// Describes the frame that an <see cref="IPostStage"/> processes.
/// </summary>
/// <param name="Width">The frame width, in physical pixels.</param>
/// <param name="Height">The frame height, in physical pixels.</param>
/// <param name="Tick">The target presentation time and refresh interval of the frame.</param>
/// <param name="Damage">The region that changed since the previous frame, in physical pixels. It is <see langword="null"/> when the caller has no damage to give.</param>
public readonly record struct PostContext(int Width, int Height, FrameTick Tick, PixmanRegion32? Damage = null);
