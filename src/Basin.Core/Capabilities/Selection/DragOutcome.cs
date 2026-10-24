namespace Basin.Capabilities;

/// <summary>
/// Lists the ways that a drag-and-drop operation can end.
/// </summary>
public enum DragOutcome
{
    /// <summary>
    /// The drag ended without a drop. The source is told that the drag was cancelled.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The user dropped on a target that accepts the data.
    /// </summary>
    Dropped,

    /// <summary>
    /// Another party took over the drag, for example a parent compositor. The local drag ends, and the source is not cancelled.
    /// </summary>
    Handed,
}
