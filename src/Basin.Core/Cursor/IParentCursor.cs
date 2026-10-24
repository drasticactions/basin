namespace Basin;

/// <summary>
/// The cursor of the host compositor that a nested compositor runs inside. A nested compositor shows its cursor through it instead of drawing it.
/// </summary>
public interface IParentCursor
{
    /// <summary>
    /// Shows an image as the host cursor over the outputs.
    /// </summary>
    /// <param name="image">The cursor image.</param>
    /// <param name="hotspotX">The horizontal hotspot in buffer pixels.</param>
    /// <param name="hotspotY">The vertical hotspot in buffer pixels.</param>
    /// <param name="scale">The buffer scale of the image.</param>
    /// <returns><see langword="false"/> when the host cannot show the image.</returns>
    bool SetCursor(IBuffer image, int hotspotX, int hotspotY, double scale = 1.0);

    /// <summary>
    /// Hides the host cursor over the outputs.
    /// </summary>
    void HideCursor();
}
