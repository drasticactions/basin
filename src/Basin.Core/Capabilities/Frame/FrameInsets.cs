namespace Basin.Capabilities;

/// <summary>
/// The thickness of the frame on each side of the client area, in logical pixels.
/// </summary>
/// <param name="Top">The thickness above the client area.</param>
/// <param name="Right">The thickness to the right of the client area.</param>
/// <param name="Bottom">The thickness below the client area.</param>
/// <param name="Left">The thickness to the left of the client area.</param>
public readonly record struct FrameInsets(int Top, int Right, int Bottom, int Left);
