using Avalonia;
using Avalonia.Media;
using Basin.SoftKeyboard.Avalonia;

namespace BasinGreeter.UI;

public sealed class FlatKeyboardSkin : ISoftKeyboardSkin
{
    private static readonly IBrush KeyFace = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3c));
    private static readonly IBrush FunctionFace = new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x2c));
    private static readonly IBrush PressedFace = new SolidColorBrush(Color.FromRgb(0x5a, 0x5a, 0x5e));
    private static readonly IBrush Back = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1a));

    private readonly SoftKeyTextCache _texts = new(FontFamily.Default);

    public void DrawBackground(DrawingContext context, Rect bounds) => context.FillRectangle(Back, bounds);

    public void DrawKey(DrawingContext context, in SoftKeyDraw key)
    {
        var rect = key.Bounds;
        var box = new Rect(rect.X + 3, rect.Y + 3, Math.Max(0, rect.Width - 6), Math.Max(0, rect.Height - 6));
        var face = key.Pressed ? PressedFace : key.Shown.IsText ? KeyFace : FunctionFace;
        context.DrawRectangle(face, null, new RoundedRect(box, 6));
        if (key.Label.Length == 0)
        {
            return;
        }

        var formatted = _texts.Get(key.Label, Math.Min(22, box.Height * 0.42), Colors.White);
        context.DrawText(formatted, new Point(box.X + ((box.Width - formatted.Width) / 2), box.Y + ((box.Height - formatted.Height) / 2)));
    }

    public void DrawOverKeys(DrawingContext context, Rect bounds)
    {
    }

    public void DrawPopup(DrawingContext context, in SoftKeyPopupDraw popup)
    {
    }
}
