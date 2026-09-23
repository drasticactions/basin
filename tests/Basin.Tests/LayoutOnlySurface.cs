using Basin.Capabilities;
using Basin.UI.Quill;
using Pixman;

namespace Basin.Tests;

internal sealed class LayoutOnlySurface(int width, int height) : IQuillUISurface
{
    public UISurfaceSize Size => new(width, height, 1.0);

    public Prowl.Quill.Canvas BeginDraw() => throw new InvalidOperationException("layout only");

    public void EndDraw()
    {
    }

    public bool Configure(int logicalWidth, int logicalHeight, double scale) => true;

    public bool TryAcquire(out UIFrame frame)
    {
        frame = default;
        return false;
    }

    public void AddObserver(IUISurfaceObserver observer)
    {
    }

    public void RemoveObserver(IUISurfaceObserver observer)
    {
    }

    public bool AcceptsInputAt(double x, double y) => false;

    public string? CursorAt(double x, double y) => null;

    public void NotifyPointerEnter(double x, double y)
    {
    }

    public void NotifyPointerMotion(uint timeMs, double x, double y)
    {
    }

    public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
    {
    }

    public void NotifyPointerAxis(uint timeMs, double dx, double dy)
    {
    }

    public void NotifyPointerLeave()
    {
    }

    public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

    public void Dispose()
    {
    }
}
