namespace Basin.Capabilities;

public interface IDragTracker
{
    DataSource? DraggingSource { get; }

    Surface? DraggingIcon { get; }

    bool StartDrag(DataSource source, Surface? icon = null);

    void EndDrag(DragOutcome outcome);

    event Action? DragChanged;
}
