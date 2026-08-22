namespace Basin.Seat;

public interface ITouchDragHandler
{
    void DragTo(double x, double y);

    void DragEnd(bool cancelled);
}
