namespace Basin.Seat;

public interface ITouchChrome
{
    bool TryPress(int id, uint timeMs, double x, double y);

    void Motion(int id, uint timeMs, double x, double y);

    void Release(int id, uint timeMs, double x, double y);

    void Cancel();
}
