namespace Basin.Seat;

public interface ITouchHitTester
{
    bool TryHit(double layoutX, double layoutY, out TouchHit hit);

    bool TryMap(object? token, double layoutX, double layoutY, out double localX, out double localY);
}
