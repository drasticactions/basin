namespace Basin.Seat;

public interface IEdgeSwipeHandler
{
    bool TryArea(double layoutX, double layoutY, out EdgeSwipeArea area);

    void Claimed(EdgeSwipeRecognizer recognizer);

    void Track(EdgeSwipeRecognizer recognizer);

    void Finished(EdgeSwipeRecognizer recognizer);
}
