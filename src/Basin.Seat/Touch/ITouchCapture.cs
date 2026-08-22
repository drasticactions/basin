namespace Basin.Seat;

public interface ITouchCapture
{
    void Motion(int id, uint timeMs, double x, double y);

    void Up(int id, uint timeMs);

    void Cancel();
}
