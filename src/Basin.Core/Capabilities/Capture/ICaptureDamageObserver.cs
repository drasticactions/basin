using Basin.Diagnostics;
using Pixman;

namespace Basin.Capabilities;

/// <summary>
/// Receives change notifications from an <see cref="IScreenCapture"/>, so that a recorder captures only when something changed.
/// </summary>
public interface ICaptureDamageObserver
{
    /// <summary>
    /// Called when a part of an output changed.
    /// </summary>
    /// <param name="output">The output that changed.</param>
    /// <param name="damage">The changed area in output buffer pixels.</param>
    void OnSourceDamaged(IOutput output, Box damage);

    /// <summary>
    /// Called when only the cursor image or its position changed. The output contents did not change.
    /// </summary>
    void OnCursorChanged();
}
