namespace Basin.Capabilities;

/// <summary>
/// Reports and controls the drag-and-drop operation of a seat, for protocols that do not see the seat.
/// </summary>
public interface IDragTracker
{
    /// <summary>
    /// Gets the data source of the drag that runs now, or <see langword="null"/> when no drag runs.
    /// </summary>
    DataSource? DraggingSource { get; }

    /// <summary>
    /// Gets the icon surface of the drag that runs now, or <see langword="null"/> when there is no drag or no icon.
    /// </summary>
    Surface? DraggingIcon { get; }

    /// <summary>
    /// Starts a drag with the pointer. A drag that runs already is cancelled first.
    /// </summary>
    /// <param name="source">The data source to drag.</param>
    /// <param name="icon">The surface that follows the pointer, or <see langword="null"/> for none.</param>
    /// <returns><see langword="true"/> when the drag started, or <see langword="false"/> when <paramref name="icon"/> already has a different role.</returns>
    bool StartDrag(DataSource source, Surface? icon = null);

    /// <summary>
    /// Ends the drag that runs now. It does nothing when no drag runs.
    /// </summary>
    /// <param name="outcome">How the drag ends.</param>
    void EndDrag(DragOutcome outcome);

    /// <summary>
    /// Occurs when a drag starts or ends.
    /// </summary>
    event Action? DragChanged;
}
