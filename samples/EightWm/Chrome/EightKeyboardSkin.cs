using Avalonia;
using Avalonia.Media;
using Basin.SoftKeyboard;
using Basin.SoftKeyboard.Avalonia;

namespace EightWm;

public sealed class EightKeyboardSkin : ISoftKeyboardSkin
{
    public const double Gap = 3;

    private static readonly IBrush Back = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
    private static readonly IBrush LetterFace = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly IBrush FunctionFace = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
    private static readonly IBrush PopupBody = new SolidColorBrush(Color.FromRgb(0x1f, 0x1f, 0x1f));
    private static readonly Pen PopupEdge = new(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1);

    private readonly IBrush _accent;
    private readonly Pen _latched;
    private readonly SoftKeyTextCache _texts = new(FontFamily.Default);

    public EightKeyboardSkin(Color accent)
    {
        Accent = accent;
        _accent = new SolidColorBrush(accent);
        _latched = new Pen(_accent, 2);
    }

    public Color Accent { get; }

    public SoftKeyTextCache Texts => _texts;

    public void DrawBackground(DrawingContext context, Rect bounds) => context.FillRectangle(Back, bounds);

    public void DrawKey(DrawingContext context, in SoftKeyDraw key)
    {
        var rect = key.Bounds;
        var face = new Rect(rect.X + (Gap / 2), rect.Y + (Gap / 2), Math.Max(0, rect.Width - Gap), Math.Max(0, rect.Height - Gap));
        var state = key.State;
        var locked = (state & SoftKeyState.Locked) != 0 || (key.Shown.Function == KeyFunction.Shift && (state & SoftKeyState.ShiftLock) != 0);
        var latched = (state & SoftKeyState.Latched) != 0 || (key.Shown.Function == KeyFunction.Shift && (state & SoftKeyState.ShiftOnce) != 0);
        IBrush fill = key.Pressed || locked ? _accent : (state & SoftKeyState.Function) != 0 ? FunctionFace : LetterFace;
        context.FillRectangle(fill, face);
        if (latched && !key.Pressed && !locked)
        {
            context.DrawRectangle(null, _latched, face.Deflate(1));
        }

        if (key.Label.Length == 0)
        {
            return;
        }

        var size = Math.Min(key.Label.Length > 2 ? 17 : 24, face.Height * 0.4);
        var text = _texts.Get(key.Label, size, Colors.White);
        context.DrawText(text, new Point(face.X + ((face.Width - text.Width) / 2), face.Y + ((face.Height - text.Height) / 2)));
    }

    public void DrawOverKeys(DrawingContext context, Rect bounds)
    {
    }

    public void DrawPopup(DrawingContext context, in SoftKeyPopupDraw popup)
    {
        var geometry = popup.Geometry;
        var top = popup.KeymapTop;
        var frame = geometry.Frame;
        context.DrawRectangle(PopupBody, PopupEdge, new Rect(frame.X, frame.Y + top, frame.Width, frame.Height));
        for (var i = 0; i < popup.Entries.Count; i++)
        {
            var cell = geometry.CellRect(i);
            var box = new Rect(cell.X + 2, cell.Y + top + 2, cell.Width - 4, cell.Height - 4);
            if (i == popup.Highlighted)
            {
                context.FillRectangle(_accent, box);
            }

            var label = SoftKeyLabels.For(popup.Engine, popup.Entries[i]);
            if (label.Length == 0)
            {
                continue;
            }

            var text = _texts.Get(label, label.Length > 2 ? 16 : 24, Colors.White);
            context.DrawText(text, new Point(box.X + ((box.Width - text.Width) / 2), box.Y + ((box.Height - text.Height) / 2)));
        }
    }
}
