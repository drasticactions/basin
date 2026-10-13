using Avalonia;
using Avalonia.Media;

namespace Basin.SoftKeyboard.Avalonia;

public interface ISoftKeyboardSkin
{
    void DrawBackground(DrawingContext context, Rect bounds);

    void DrawKey(DrawingContext context, in SoftKeyDraw key);

    void DrawOverKeys(DrawingContext context, Rect bounds);

    void DrawPopup(DrawingContext context, in SoftKeyPopupDraw popup);
}
