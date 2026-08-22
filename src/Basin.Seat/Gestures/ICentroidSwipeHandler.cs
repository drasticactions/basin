namespace Basin.Seat;

public interface ICentroidSwipeHandler
{
    bool Begin(double centroidX, double centroidY, uint timeMs);

    void Update(double dx, double dy, uint timeMs);

    void End(bool cancelled, uint timeMs);
}
