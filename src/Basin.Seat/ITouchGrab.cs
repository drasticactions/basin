using Wayland;

namespace Basin.Seat;

public interface ITouchGrab
{
    uint Down(Surface surface, uint timeMs, int id, double x, double y);

    void Up(uint timeMs, int id);

    void Motion(uint timeMs, int id, double x, double y);

    void Frame();

    void Cancel();
}
