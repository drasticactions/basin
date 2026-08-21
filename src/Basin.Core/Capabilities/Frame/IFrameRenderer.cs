namespace Basin.Capabilities;

public interface IFrameRenderer
{
    FrameInsets Measure(in FrameState state, double scale);

    void Draw(IUISurface surface, in Box clientBox, in FrameState state, in FrameInteraction interaction);

    FramePart PartAt(double x, double y, in FrameState state, double scale);

    string? CursorFor(FramePart part) => null;

    Box PartBounds(FramePart part) => default;

    UISurfaceSize MeasureMenu(in FrameState state, double scale) => default;

    void DrawMenu(IUISurface surface, in FrameState state, int hotItem)
    {
    }

    int MenuItemAt(double x, double y, in FrameState state, double scale) => -1;

    FrameAction? MenuItemAction(int item, in FrameState state) => null;
}
