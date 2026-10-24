using Basin.Scene;

namespace Basin.Effects;

/// <summary>
/// Advances one running animation by one frame.
/// </summary>
/// <param name="stack">The transform stack of the window that the animation runs on.</param>
/// <param name="tick">The frame tick that gives the predicted present time.</param>
/// <returns><see langword="true"/> while the animation needs more frames, <see langword="false"/> when it has finished.</returns>
public delegate bool AnimationStep(TransformStack stack, in FrameTick tick);
