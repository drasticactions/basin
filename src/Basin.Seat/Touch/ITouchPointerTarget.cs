namespace Basin.Seat;

public interface ITouchPointerTarget
{
    void Warp(uint timeMs, double x, double y);

    void Button(uint timeMs, uint button, bool pressed);
}
