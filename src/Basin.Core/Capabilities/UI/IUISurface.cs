using Pixman;

namespace Basin.Capabilities;

public interface IUISurface : IDisposable
{
    UISurfaceSize Size { get; }

    bool Configure(int logicalWidth, int logicalHeight, double scale);

    void SetPosition(double x, double y)
    {
    }

    double PositionX => 0;

    double PositionY => 0;

    bool TryAcquire(out UIFrame frame);

    void AddObserver(IUISurfaceObserver observer);

    void RemoveObserver(IUISurfaceObserver observer);

    bool AcceptsInputAt(double x, double y);

    string? CursorAt(double x, double y);

    void NotifyPointerEnter(double x, double y);

    void NotifyPointerMotion(uint timeMs, double x, double y);

    void NotifyPointerButton(uint timeMs, uint button, bool pressed);

    void NotifyPointerAxis(uint timeMs, double dx, double dy);

    void NotifyPointerLeave();

    void NotifyKeyboardEnter(ReadOnlySpan<uint> pressed)
    {
    }

    void NotifyKey(uint timeMs, uint key, bool pressed)
    {
    }

    void NotifyModifiers(uint depressed, uint latched, uint locked, uint group)
    {
    }

    void NotifyKeyboardLeave()
    {
    }

    void NotifyTouchDown(uint timeMs, int id, double x, double y)
    {
    }

    void NotifyTouchMotion(uint timeMs, int id, double x, double y)
    {
    }

    void NotifyTouchUp(uint timeMs, int id)
    {
    }

    void NotifyTouchCancel()
    {
    }

    bool WantsTextInput => false;

    void NotifyTextCommit(ReadOnlySpan<char> text)
    {
    }

    void NotifyPreedit(ReadOnlySpan<char> text, int cursorBegin, int cursorEnd)
    {
    }

    IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity);
}
