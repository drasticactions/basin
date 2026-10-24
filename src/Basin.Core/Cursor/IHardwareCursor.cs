using System.Diagnostics.CodeAnalysis;
using Pixman;

namespace Basin;

/// <summary>
/// An output that can show a cursor on a hardware cursor plane. Coordinates are in physical output pixels.
/// </summary>
public interface IHardwareCursor
{
    /// <summary>
    /// Shows a buffer on the cursor plane, or hides the plane. The output locks the buffer while it shows it.
    /// </summary>
    /// <param name="buffer">The cursor image, or <see langword="null"/> to hide the cursor.</param>
    /// <param name="hotspotX">The horizontal hotspot in buffer pixels.</param>
    /// <param name="hotspotY">The vertical hotspot in buffer pixels.</param>
    /// <returns><see langword="false"/> when the plane cannot show the buffer. The caller must then draw the cursor in the scene.</returns>
    bool SetCursor(IBuffer? buffer, int hotspotX, int hotspotY);

    /// <summary>
    /// Moves the hotspot of the cursor.
    /// </summary>
    /// <param name="x">The horizontal position in physical output pixels.</param>
    /// <param name="y">The vertical position in physical output pixels.</param>
    void MoveCursor(int x, int y);

    /// <summary>
    /// Gets a value indicating whether a cursor change waits for the next output commit. When it is true, the caller must request a commit.
    /// </summary>
    bool CursorAwaitingFrame => false;

    /// <summary>
    /// Gets the cursor that the plane shows now, for a screenshot that includes planes.
    /// </summary>
    /// <param name="buffer">The cursor buffer, or <see langword="null"/> when no cursor is visible.</param>
    /// <param name="destination">The position and size of the cursor in physical output pixels.</param>
    /// <returns><see langword="false"/> when no cursor is visible on the plane.</returns>
    bool TryPresentedCursor([NotNullWhen(true)] out IBuffer? buffer, out Box destination)
    {
        buffer = null;
        destination = default;
        return false;
    }
}
