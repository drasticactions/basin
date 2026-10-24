using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// A cursor image in a buffer that a cursor plane or a scene can show.
/// </summary>
/// <param name="Buffer">The buffer that holds the image. It can be larger than the image, with the image at its top-left corner.</param>
/// <param name="Width">The width of the image in buffer pixels.</param>
/// <param name="Height">The height of the image in buffer pixels.</param>
/// <param name="HotspotX">The horizontal hotspot in buffer pixels.</param>
/// <param name="HotspotY">The vertical hotspot in buffer pixels.</param>
/// <param name="Clipped">A value that is true when the image is too large for the cursor plane buffer and must be drawn by the scene.</param>
public readonly record struct CursorImage(
    IBuffer Buffer,
    int Width,
    int Height,
    int HotspotX,
    int HotspotY,
    bool Clipped = false);
