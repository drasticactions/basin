namespace Basin.Seat;

public interface ITouchInteractionObserver
{
    void OnTouchInteraction(int id, TouchTargetKind kind, Surface? surface);
}
