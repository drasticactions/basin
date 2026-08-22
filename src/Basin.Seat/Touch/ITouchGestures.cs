namespace Basin.Seat;

public interface ITouchGestures
{
    TouchGestureVerdict Down(int id, uint timeMs, double x, double y);

    TouchGestureVerdict Motion(int id, uint timeMs, double x, double y);

    TouchGestureVerdict Up(int id, uint timeMs);

    void Cancel();

    int TakeWithheld(Span<EdgeSwipeSample> into);
}
