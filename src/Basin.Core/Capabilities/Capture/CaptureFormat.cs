namespace Basin.Capabilities;

/// <summary>
/// Describes the size and the pixel format of a capture.
/// </summary>
/// <param name="Width">The width in buffer pixels.</param>
/// <param name="Height">The height in buffer pixels.</param>
/// <param name="Format">The pixel format that the capture writes.</param>
public readonly record struct CaptureFormat(int Width, int Height, DrmFormat Format)
{
    /// <summary>
    /// Gets the row length in bytes of a tightly packed buffer of this format.
    /// </summary>
    public int Stride => Width * Format.BytesPerPixel();
}
