namespace Basin.UI.Skia;

public interface ISkiaPointerHandler
{
    void PointerEnter(double x, double y);

    void PointerMotion(uint timeMs, double x, double y);

    void PointerButton(uint timeMs, uint button, bool pressed);

    void PointerAxis(uint timeMs, double dx, double dy)
    {
    }

    void PointerLeave();

    string? CursorAt(double x, double y) => null;
}
